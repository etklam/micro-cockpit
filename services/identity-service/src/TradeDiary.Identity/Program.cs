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
    builder.Configuration.GetConnectionString("Identity") ??
    "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
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
builder.Services.AddAuthorization();
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
    var registrationKey = config["Auth:LocalRegistrationKey"];
    if (string.IsNullOrEmpty(registrationKey)) return Results.Problem("not_found", statusCode: 404);
    if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(request.Headers["X-Registration-Key"].FirstOrDefault() ?? ""),
        Encoding.UTF8.GetBytes(registrationKey))) return Results.Problem("not_found", statusCode: 404);
    var email = input.Email.Trim().ToLowerInvariant();
    if (!email.Contains('@') || input.Password.Length < 12 || string.IsNullOrWhiteSpace(input.DisplayName))
        return Results.Problem("invalid_registration", statusCode: 400);
    if (input.BaseCurrency.Length != 3 || string.IsNullOrWhiteSpace(input.Timezone))
        return Results.Problem("invalid_locale", statusCode: 400);

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
        user.Parameters.AddWithValue(input.DisplayName.Trim()); user.Parameters.AddWithValue(input.Timezone);
        user.Parameters.AddWithValue(input.BaseCurrency.ToUpperInvariant());
        await user.ExecuteNonQueryAsync();
        await using var credential = new NpgsqlCommand("INSERT INTO identity.user_credentials VALUES ($1,$2,$3,$4)", connection, transaction);
        credential.Parameters.AddWithValue(id); credential.Parameters.AddWithValue(salt);
        credential.Parameters.AddWithValue(hash); credential.Parameters.AddWithValue(iterations);
        await credential.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return Results.Created("/internal/auth/me", new RegisterResponse(id, email, input.DisplayName, input.Timezone, input.BaseCurrency.ToUpperInvariant()));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        await transaction.RollbackAsync();
        return Results.Problem("email_exists", statusCode: 409);
    }
})
.Produces<RegisterResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

app.MapPost("/internal/auth/login", async (LoginRequest input, NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("""
        SELECT u.id,u.email,u.display_name,u.timezone,u.base_currency,u.role,u.account_type,u.status,u.status_version,
               c.password_salt,c.password_hash,c.iterations
        FROM identity.users u JOIN identity.user_credentials c ON c.user_id=u.id WHERE u.email=$1
        """);
    command.Parameters.AddWithValue(input.Email.Trim().ToLowerInvariant());
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Unauthorized();
    var candidate = Rfc2898DeriveBytes.Pbkdf2(input.Password, (byte[])reader[9], reader.GetInt32(11), HashAlgorithmName.SHA256, 32);
    if (!CryptographicOperations.FixedTimeEquals(candidate, (byte[])reader[10]) || reader.GetString(7) != "active") return Results.Unauthorized();
    var user = ReadUser(reader);
    await reader.CloseAsync();
    return Results.Ok(await CreateTokenPair(db, user, Guid.NewGuid(), signingKey, issuer, audience));
})
.Produces<AuthTokens>(200).ProducesProblem(401);

app.MapPost("/internal/auth/refresh", async (RefreshRequest input, NpgsqlDataSource db) =>
{
    byte[] supplied;
    try { supplied = Convert.FromBase64String(input.RefreshToken); }
    catch { return Results.Unauthorized(); }
    var tokenHash = SHA256.HashData(supplied);
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand("""
        SELECT r.id,r.user_id,r.family_id,r.expires_at,r.used_at,r.revoked_at,
               u.email,u.display_name,u.timezone,u.base_currency,u.role,u.account_type,u.status,u.status_version
        FROM identity.refresh_tokens r JOIN identity.users u ON u.id=r.user_id
        WHERE r.token_hash=$1 FOR UPDATE
        """, connection, transaction);
    command.Parameters.AddWithValue(tokenHash);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Unauthorized();
    var tokenId = reader.GetGuid(0); var familyId = reader.GetGuid(2);
    var invalid = reader.GetDateTime(3) <= DateTime.UtcNow || !reader.IsDBNull(4) || !reader.IsDBNull(5) || reader.GetString(12) != "active";
    var user = new AuthUser(reader.GetGuid(1), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetInt32(13));
    await reader.CloseAsync();
    if (invalid)
    {
        await using var revoke = new NpgsqlCommand("UPDATE identity.refresh_tokens SET revoked_at=coalesce(revoked_at,now()) WHERE family_id=$1", connection, transaction);
        revoke.Parameters.AddWithValue(familyId); await revoke.ExecuteNonQueryAsync(); await transaction.CommitAsync();
        return Results.Unauthorized();
    }
    await using var use = new NpgsqlCommand("UPDATE identity.refresh_tokens SET used_at=now() WHERE id=$1", connection, transaction);
    use.Parameters.AddWithValue(tokenId); await use.ExecuteNonQueryAsync(); await transaction.CommitAsync();
    return Results.Ok(await CreateTokenPair(db, user, familyId, signingKey, issuer, audience));
})
.Produces<AuthTokens>(200).ProducesProblem(401);

app.MapPost("/internal/auth/logout", async (RefreshRequest input, NpgsqlDataSource db) =>
{
    try
    {
        var hash = SHA256.HashData(Convert.FromBase64String(input.RefreshToken));
        await using var command = db.CreateCommand("""
            UPDATE identity.refresh_tokens SET revoked_at=coalesce(revoked_at,now())
            WHERE family_id=(SELECT family_id FROM identity.refresh_tokens WHERE token_hash=$1)
            """);
        command.Parameters.AddWithValue(hash); await command.ExecuteNonQueryAsync();
    }
    catch { }
    return Results.NoContent();
})
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
    var user = ReadUser(reader); var scopes = reader.GetFieldValue<string[]>(9); return Results.Ok(new ApiKeyTokenResponse(CreateAccessToken(user, signingKey, issuer, audience, scopes), DateTime.UtcNow.AddMinutes(15)));
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
    await using var command = db.CreateCommand("SELECT id,email,display_name,timezone,base_currency,role,account_type,status,status_version FROM identity.users WHERE id=$1 AND status='active'");
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadUser(reader)) : Results.Problem("not_found", statusCode: 404);
})
.RequireAuthorization()
.Produces<AuthUser>(200).ProducesProblem(401).ProducesProblem(404);

app.Run();

static AuthUser ReadUser(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8));

static RSA LoadSigningKey(string? path)
{
    var rsa = RSA.Create();
    if (string.IsNullOrWhiteSpace(path)) { rsa.KeySize = 3072; return rsa; }
    if (File.Exists(path)) { rsa.ImportFromPem(File.ReadAllText(path)); return rsa; }
    rsa.KeySize = 3072; Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem()); return rsa;
}

static async Task<AuthTokens> CreateTokenPair(NpgsqlDataSource db, AuthUser user, Guid familyId, SecurityKey key, string issuer, string audience)
{
    var now = DateTime.UtcNow;
    var accessToken = CreateAccessToken(user, key, issuer, audience, []);
    var refreshBytes = RandomNumberGenerator.GetBytes(32);
    await using var command = db.CreateCommand("INSERT INTO identity.refresh_tokens (id,user_id,family_id,token_hash,expires_at) VALUES ($1,$2,$3,$4,$5)");
    command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(user.Id); command.Parameters.AddWithValue(familyId);
    command.Parameters.AddWithValue(SHA256.HashData(refreshBytes)); command.Parameters.AddWithValue(now.AddDays(30));
    await command.ExecuteNonQueryAsync();
    return new AuthTokens(accessToken, now.AddMinutes(15), Convert.ToBase64String(refreshBytes));
}

static string CreateAccessToken(AuthUser user, SecurityKey key, string issuer, string audience, IReadOnlyCollection<string> scopes)
{
    var now = DateTime.UtcNow;
    var claims = new[] {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role), new Claim("account_type", user.AccountType), new Claim("status_version", user.StatusVersion.ToString()),
        new Claim("timezone", user.Timezone), new Claim("base_currency", user.BaseCurrency.Trim())
    }.Concat(scopes.Select(scope => new Claim("scope", scope)));
    var jwt = new JwtSecurityToken(issuer, audience, claims, now, now.AddMinutes(15), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

record RegisterRequest(string Email, string Password, string DisplayName, string Timezone, string BaseCurrency);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record AgentRequest(string Name, string DisplayName, string Timezone, string BaseCurrency, List<string> Scopes, DateTime? ExpiresAt);
record ApiKeyTokenRequest(string ApiKey);
record AuthUser(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency, string Role, string AccountType, string Status, int StatusVersion);
record AuthTokens(string AccessToken, DateTime ExpiresAt, string RefreshToken);
record RegisterResponse(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency);
record AgentResponse(Guid UserId, Guid KeyId, string ApiKey, List<string> Scopes);
record ApiKeyTokenResponse(string AccessToken, DateTime ExpiresAt);
record SsoProvidersResponse(string[] EnabledProviders);

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
        var path = "/" + (context.Description.RelativePath ?? string.Empty);
        var scheme = path.Contains("/internal/admin/", StringComparison.Ordinal)
                     || path.Contains("/internal/worker/", StringComparison.Ordinal)
                     || path.Contains("/internal/events/", StringComparison.Ordinal)
            ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}
