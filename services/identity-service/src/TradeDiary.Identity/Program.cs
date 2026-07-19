using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var issuer = builder.Configuration["Jwt:Issuer"] ?? "trade-diary-identity";
var audience = builder.Configuration["Jwt:Audience"] ?? "trade-diary-services";
var rsa = LoadSigningKey(builder.Configuration["Jwt:PrivateKeyPath"]);
// ponytail: kid is a deterministic SHA-256 thumbprint of the RSA public key (SubjectPublicKeyInfo),
// so the same persisted key yields the same kid across restarts; only a key rotation changes it.
var signingKey = new RsaSecurityKey(rsa) { KeyId = SigningKeyIdentity.GetKeyId(rsa) };
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Identity") ?? throw new InvalidOperationException("Connection string 'Identity' is required.")));
builder.Services.AddSingleton(new RefreshTokenFamilyOptions(signingKey, issuer, audience));
builder.Services.AddSingleton<RefreshTokenFamily>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization(options => { var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build(); options.DefaultPolicy = humanOnly; options.FallbackPolicy = humanOnly; });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); }
    catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); }
}).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "identity-service", version = "0.1.0" })).AllowAnonymous();
app.MapGet("/internal/auth/sso/providers", () => Results.Ok(new SsoProvidersResponse(Array.Empty<string>())))
    .AllowAnonymous()
    .Produces<SsoProvidersResponse>(200);
app.MapGet("/.well-known/openid-configuration", (HttpRequest request) => Results.Ok(new
{
    issuer,
    jwks_uri = $"{request.Scheme}://{request.Host}/.well-known/jwks.json"
})).AllowAnonymous();
app.MapGet("/.well-known/jwks.json", () =>
{
    var p = rsa.ExportParameters(false);
    return Results.Ok(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = signingKey.KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
}).AllowAnonymous();

app.MapPost("/internal/auth/register", async (RegisterRequest input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    var allowPublicRegistration = config.GetValue("Auth:AllowPublicRegistration", false);
    var registrationKey = config["Auth:LocalRegistrationKey"];
    if (!allowPublicRegistration)
    {
        if (string.IsNullOrEmpty(registrationKey)) return Results.Problem("not_found", statusCode: 404);
        var suppliedKey = Encoding.UTF8.GetBytes(request.Headers["X-Registration-Key"].FirstOrDefault() ?? "");
        var expectedKey = Encoding.UTF8.GetBytes(registrationKey);
        if (suppliedKey.Length != expectedKey.Length || !CryptographicOperations.FixedTimeEquals(suppliedKey, expectedKey))
            return Results.Problem("not_found", statusCode: 404);
    }
    if (!TryNormalizeRegistration(input, out var email, out var displayName, out var timezone, out var baseCurrency, out var problem))
        return Results.Problem(problem, statusCode: 400);

    var id = Guid.NewGuid();
    var salt = RandomNumberGenerator.GetBytes(16);
    const int iterations = 210_000;
    var hash = Rfc2898DeriveBytes.Pbkdf2(input.Password, salt, iterations, HashAlgorithmName.SHA256, 32);
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        await using var user = new NpgsqlCommand("""
            INSERT INTO identity.users (id,email,display_name,timezone,base_currency)
            VALUES ($1,$2,$3,$4,$5)
            """, connection, transaction);
        user.Parameters.AddWithValue(id); user.Parameters.AddWithValue(email);
        user.Parameters.AddWithValue(displayName); user.Parameters.AddWithValue(timezone);
        user.Parameters.AddWithValue(baseCurrency);
        await user.ExecuteNonQueryAsync();
        await using var credential = new NpgsqlCommand("INSERT INTO identity.user_credentials VALUES ($1,$2,$3,$4)", connection, transaction);
        credential.Parameters.AddWithValue(id); credential.Parameters.AddWithValue(salt);
        credential.Parameters.AddWithValue(hash); credential.Parameters.AddWithValue(iterations);
        await credential.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return Results.Created("/internal/auth/me", new RegisterResponse(id, email, displayName, timezone, baseCurrency));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        await transaction.RollbackAsync();
        return Results.Problem("email_exists", statusCode: 409);
    }
})
.AllowAnonymous()
.Produces<RegisterResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

app.MapPost("/internal/auth/login", async (LoginRequest input, NpgsqlDataSource db, RefreshTokenFamily refreshTokens) =>
{
    // Bound password length before PBKDF2 so oversized bodies cannot force unbounded work.
    if (input.Password is null || input.Password.Length is < 1 or > 256)
        return Results.Unauthorized();
    var email = (input.Email ?? string.Empty).Trim().ToLowerInvariant();
    if (email.Length is < 3 or > 254)
        return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT u.id,u.email,u.display_name,u.timezone,u.base_currency,u.role,u.account_type,u.status,u.status_version,
               c.password_salt,c.password_hash,c.iterations
        FROM identity.users u JOIN identity.user_credentials c ON c.user_id=u.id WHERE u.email=$1
        """);
    command.Parameters.AddWithValue(email);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Unauthorized();
    var candidate = Rfc2898DeriveBytes.Pbkdf2(input.Password, (byte[])reader[9], reader.GetInt32(11), HashAlgorithmName.SHA256, 32);
    if (!CryptographicOperations.FixedTimeEquals(candidate, (byte[])reader[10]) || reader.GetString(7) != "active") return Results.Unauthorized();
    var user = ReadUser(reader);
    await reader.CloseAsync();
    return Results.Ok(await refreshTokens.IssueAsync(user, Guid.NewGuid()));
})
.AllowAnonymous()
.Produces<AuthTokens>(200).ProducesProblem(401);

app.MapPost("/internal/auth/refresh", async (RefreshRequest input, RefreshTokenFamily refreshTokens) =>
{
    var result = await refreshTokens.RotateAsync(input.RefreshToken);
    return result.Status == RefreshTokenRotationStatus.Rotated
        ? Results.Ok(result.Tokens)
        : Results.Unauthorized();
})
.AllowAnonymous()
.Produces<AuthTokens>(200).ProducesProblem(401);

app.MapPost("/internal/auth/logout", async (RefreshRequest input, RefreshTokenFamily refreshTokens) =>
{
    await refreshTokens.RevokeFamilyAsync(input.RefreshToken);
    return Results.NoContent();
})
.AllowAnonymous()
.Produces(204);

app.MapPost("/internal/auth/agents", async (AgentRequest input, ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var creator)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.DisplayName) || input.BaseCurrency.Length != 3 || input.Scopes.Count == 0 || input.Scopes.Any(s => s is not ("diary:read" or "diary:write" or "research:read")))
        return Results.Problem("invalid_agent", statusCode: 400);
    var userId = Guid.NewGuid(); var keyId = Guid.NewGuid(); var raw = RandomNumberGenerator.GetBytes(32); var email = $"agent-{userId:N}@local.invalid";
    await using var connection = await db.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
    await using (var user = new NpgsqlCommand("INSERT INTO identity.users(id,email,display_name,timezone,base_currency,account_type) VALUES($1,$2,$3,$4,$5,'agent')", connection, tx))
    { user.Parameters.AddWithValue(userId); user.Parameters.AddWithValue(email); user.Parameters.AddWithValue(input.DisplayName.Trim()); user.Parameters.AddWithValue(input.Timezone); user.Parameters.AddWithValue(input.BaseCurrency.ToUpperInvariant()); await user.ExecuteNonQueryAsync(); }
    await using (var key = new NpgsqlCommand("INSERT INTO identity.api_keys(id,user_id,created_by,name,key_hash,scopes,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7)", connection, tx))
    { key.Parameters.AddWithValue(keyId); key.Parameters.AddWithValue(userId); key.Parameters.AddWithValue(creator); key.Parameters.AddWithValue(input.Name.Trim()); key.Parameters.AddWithValue(SHA256.HashData(raw)); key.Parameters.AddWithValue(input.Scopes.ToArray()); key.Parameters.AddWithValue((object?)input.ExpiresAt?.ToUniversalTime() ?? DBNull.Value); await key.ExecuteNonQueryAsync(); }
    await tx.CommitAsync(); return Results.Created($"/internal/auth/agents/{userId}", new AgentResponse(userId, keyId, Convert.ToBase64String(raw), input.Scopes));
})
.RequireAuthorization()
.Produces<AgentResponse>(201).ProducesProblem(400).ProducesProblem(401);

app.MapPost("/internal/auth/api-key/token", async (ApiKeyTokenRequest input, NpgsqlDataSource db) =>
{
    byte[] raw; try { raw = Convert.FromBase64String(input.ApiKey); } catch { return Results.Unauthorized(); }
    await using var command = db.CreateCommand("""
        SELECT u.id,u.email,u.display_name,u.timezone,u.base_currency,u.role,u.account_type,u.status,u.status_version,k.scopes
        FROM identity.api_keys k JOIN identity.users u ON u.id=k.user_id
        WHERE k.key_hash=$1 AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at>now()) AND u.status='active'
        """); command.Parameters.AddWithValue(SHA256.HashData(raw)); await using var reader = await command.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return Results.Unauthorized();
    var user = ReadUser(reader); var scopes = reader.GetFieldValue<string[]>(9); return Results.Ok(new ApiKeyTokenResponse(IdentityAccessTokenIssuer.Create(user, signingKey, issuer, audience, scopes), DateTime.UtcNow.AddMinutes(15)));
})
.AllowAnonymous()
.Produces<ApiKeyTokenResponse>(200).ProducesProblem(401);

app.MapDelete("/internal/auth/api-keys/{id:guid}", async (Guid id, ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var creator)) return Results.Unauthorized();
    await using var command = db.CreateCommand("UPDATE identity.api_keys SET revoked_at=now() WHERE id=$1 AND created_by=$2 AND revoked_at IS NULL"); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(creator);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.RequireAuthorization()
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/auth/me", async (ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT id,email,display_name,timezone,base_currency,role,account_type,status,status_version,appearance,locale
        FROM identity.users WHERE id=$1 AND status='active'
        """);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadUserWithAppearance(reader)) : Results.Problem("not_found", statusCode: 404);
})
.RequireAuthorization()
.Produces<AuthUser>(200).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/auth/settings", async (ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT email, display_name, timezone, base_currency, appearance, locale, updated_at
        FROM identity.users WHERE id=$1 AND status='active'
        """);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
    return Results.Ok(ReadSettings(reader));
})
.RequireAuthorization()
.Produces<UserSettingsResponse>(200).ProducesProblem(401).ProducesProblem(404);

app.MapPut("/internal/auth/settings", async (UserSettingsWrite input, ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    if (!TryNormalizeSettings(input, out var displayName, out var timezone, out var baseCurrency, out var appearance, out var locale, out var problem))
        return Results.Problem(problem, statusCode: 400);

    await using var command = db.CreateCommand("""
        UPDATE identity.users
        SET display_name=$2, timezone=$3, base_currency=$4, appearance=$5, locale=$6, updated_at=now()
        WHERE id=$1 AND status='active' AND account_type <> 'agent'
        RETURNING email, display_name, timezone, base_currency, appearance, locale, updated_at
        """);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(displayName);
    command.Parameters.AddWithValue(timezone);
    command.Parameters.AddWithValue(baseCurrency);
    command.Parameters.AddWithValue(appearance);
    command.Parameters.AddWithValue(locale);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
    return Results.Ok(ReadSettings(reader));
})
.RequireAuthorization()
.Produces<UserSettingsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

// Explicit Identity contract for display names. Callers must already know the user IDs
// (e.g. from Partner links). Does not expose email or allow search.
app.MapGet("/internal/users/display-names", async (string? ids, ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out _)) return Results.Unauthorized();
    var requested = ParseUserIds(ids);
    if (requested.Count == 0) return Results.Ok(new CollectionResponse<DisplayNameItem>([]));
    if (requested.Count > 50) return Results.Problem("too_many_ids", statusCode: 400);

    await using var command = db.CreateCommand("""
        SELECT id, display_name FROM identity.users
        WHERE status = 'active' AND id = ANY($1)
        """);
    command.Parameters.AddWithValue(requested.ToArray());
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<DisplayNameItem>();
    while (await reader.ReadAsync())
    {
        // Nullable-safe: treat DB null/blank as absent so callers can degrade.
        string? name = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (string.IsNullOrWhiteSpace(name)) name = null;
        items.Add(new DisplayNameItem(reader.GetGuid(0), name));
    }
    return Results.Ok(new CollectionResponse<DisplayNameItem>(items));
})
.RequireAuthorization()
.Produces<CollectionResponse<DisplayNameItem>>(200).ProducesProblem(400).ProducesProblem(401);

app.Run();

// Login/API-key readers omit appearance/locale (column 9 is credentials/scopes there). Defaults are fine for JWT.
static AuthUser ReadUser(NpgsqlDataReader reader) => new(
    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8));

static AuthUser ReadUserWithAppearance(NpgsqlDataReader reader) => new(
    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.GetString(9), reader.GetString(10));

static UserSettingsResponse ReadSettings(NpgsqlDataReader reader) => new(
    reader.GetString(0),
    reader.GetString(1),
    reader.GetString(2),
    reader.GetString(3).Trim(),
    reader.GetString(4),
    reader.GetString(5),
    DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc));

static bool TryNormalizeSettings(
    UserSettingsWrite input,
    out string displayName,
    out string timezone,
    out string baseCurrency,
    out string appearance,
    out string locale,
    out string problem)
{
    displayName = string.Empty;
    timezone = string.Empty;
    baseCurrency = string.Empty;
    appearance = string.Empty;
    locale = string.Empty;
    problem = "invalid_settings";

    if (input.DisplayName is null || input.Timezone is null || input.BaseCurrency is null || input.Appearance is null || input.Locale is null)
        return false;

    displayName = input.DisplayName.Trim();
    if (displayName.Length is < 1 or > 100 || ContainsControlCharacters(displayName))
    {
        problem = "invalid_display_name";
        return false;
    }

    var timezoneRaw = input.Timezone.Trim();
    if (timezoneRaw.Length is < 1 or > 100 || ContainsControlCharacters(timezoneRaw)
        || !TimeZoneInfo.TryFindSystemTimeZoneById(timezoneRaw, out var tz))
    {
        problem = "invalid_timezone";
        return false;
    }
    timezone = tz.Id;

    var currency = input.BaseCurrency.Trim().ToUpperInvariant();
    if (currency.Length != 3 || !currency.All(static c => c is >= 'A' and <= 'Z'))
    {
        problem = "invalid_currency";
        return false;
    }
    baseCurrency = currency;

    appearance = input.Appearance.Trim().ToLowerInvariant();
    if (appearance is not ("system" or "light" or "dark"))
    {
        problem = "invalid_appearance";
        return false;
    }

    locale = input.Locale.Trim();
    if (locale is not ("en" or "zh-Hant"))
    {
        problem = "invalid_locale";
        return false;
    }
    return true;
}

static bool TryNormalizeRegistration(
    RegisterRequest input,
    out string email,
    out string displayName,
    out string timezone,
    out string baseCurrency,
    out string problem)
{
    email = string.Empty;
    displayName = string.Empty;
    timezone = string.Empty;
    baseCurrency = string.Empty;
    problem = "invalid_registration";

    if (input.Email is null || input.Password is null || input.DisplayName is null || input.Timezone is null || input.BaseCurrency is null)
        return false;

    email = input.Email.Trim().ToLowerInvariant();
    if (!IsValidEmail(email) || input.Password.Length is < 12 or > 256 || ContainsControlCharacters(input.Password))
        return false;

    displayName = input.DisplayName.Trim();
    if (displayName.Length is < 1 or > 100 || ContainsControlCharacters(displayName))
        return false;

    timezone = input.Timezone.Trim();
    if (timezone.Length is < 1 or > 100 || ContainsControlCharacters(timezone) || !TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
    {
        problem = "invalid_locale";
        return false;
    }

    var currency = input.BaseCurrency.Trim().ToUpperInvariant();
    if (currency.Length != 3 || !currency.All(static c => c is >= 'A' and <= 'Z'))
    {
        problem = "invalid_locale";
        return false;
    }

    baseCurrency = currency;
    return true;
}

static bool IsValidEmail(string email)
{
    if (email.Length is < 3 or > 254 || ContainsControlCharacters(email))
        return false;
    try
    {
        var address = new System.Net.Mail.MailAddress(email);
        return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
    }
    catch (FormatException)
    {
        return false;
    }
}

static bool ContainsControlCharacters(string value)
{
    foreach (var c in value)
    {
        if (char.IsControl(c)) return true;
    }
    return false;
}

static List<Guid> ParseUserIds(string? ids)
{
    var result = new List<Guid>();
    if (string.IsNullOrWhiteSpace(ids)) return result;
    foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Guid.TryParse(part, out var id) && !result.Contains(id)) result.Add(id);
    }
    return result;
}

static RSA LoadSigningKey(string? path)
{
    var rsa = RSA.Create();
    if (string.IsNullOrWhiteSpace(path)) { rsa.KeySize = 3072; return rsa; }
    if (File.Exists(path)) { rsa.ImportFromPem(File.ReadAllText(path)); return rsa; }
    rsa.KeySize = 3072; Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem()); return rsa;
}

record RegisterRequest(string Email, string Password, string DisplayName, string Timezone, string BaseCurrency);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record AgentRequest(string Name, string DisplayName, string Timezone, string BaseCurrency, List<string> Scopes, DateTime? ExpiresAt);
record ApiKeyTokenRequest(string ApiKey);
record AuthUser(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency, string Role, string AccountType, string Status, int StatusVersion, string Appearance = "system", string Locale = "en");
record AuthTokens(string AccessToken, DateTime ExpiresAt, string RefreshToken);
record RegisterResponse(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency);
record AgentResponse(Guid UserId, Guid KeyId, string ApiKey, List<string> Scopes);
record ApiKeyTokenResponse(string AccessToken, DateTime ExpiresAt);
record SsoProvidersResponse(string[] EnabledProviders);
record UserSettingsResponse(string Email, string DisplayName, string Timezone, string BaseCurrency, string Appearance, string Locale, DateTime UpdatedAt);
record UserSettingsWrite(string DisplayName, string Timezone, string BaseCurrency, string Appearance, string Locale);
record DisplayNameItem(Guid UserId, string? DisplayName);
record CollectionResponse<T>(List<T> Items);

// ponytail: shared OpenAPI security wiring — bearerAuth for user routes, serviceKey for internal admin/worker/events.
// Duplicated per service intentionally: no shared kernel is allowed across services.
sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
        document.Components.SecuritySchemes["serviceKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Service-Key"
        };
        return Task.CompletedTask;
    }
}

sealed class SecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<AllowAnonymousAttribute>().Any()) return Task.CompletedTask;
        var scheme = metadata.OfType<IAuthorizeData>().Any(data => data.Policy == "serviceKey")
            ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}
