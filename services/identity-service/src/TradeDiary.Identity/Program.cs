using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var issuer = builder.Configuration["Jwt:Issuer"] ?? "trade-diary-identity";
var audience = builder.Configuration["Jwt:Audience"] ?? "trade-diary-services";
var rsa = RSA.Create(3072);
var signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
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
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); }
    catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); }
});
app.MapGet("/version", () => Results.Ok(new { service = "identity-service", version = "0.1.0" }));
app.MapGet("/internal/auth/sso/providers", () => Results.Ok(new { enabledProviders = Array.Empty<string>() }));
app.MapGet("/.well-known/openid-configuration", (HttpRequest request) => Results.Ok(new
{
    issuer,
    jwks_uri = $"{request.Scheme}://{request.Host}/.well-known/jwks.json"
}));
app.MapGet("/.well-known/jwks.json", () =>
{
    var p = rsa.ExportParameters(false);
    return Results.Ok(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = signingKey.KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
});

app.MapPost("/internal/auth/register", async (RegisterRequest input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    var registrationKey = config["Auth:LocalRegistrationKey"];
    if (string.IsNullOrEmpty(registrationKey)) return Results.NotFound();
    if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(request.Headers["X-Registration-Key"].FirstOrDefault() ?? ""),
        Encoding.UTF8.GetBytes(registrationKey))) return Results.NotFound();
    var email = input.Email.Trim().ToLowerInvariant();
    if (!email.Contains('@') || input.Password.Length < 12 || string.IsNullOrWhiteSpace(input.DisplayName))
        return Results.BadRequest(new { error = "invalid_registration" });
    if (input.BaseCurrency.Length != 3 || string.IsNullOrWhiteSpace(input.Timezone))
        return Results.BadRequest(new { error = "invalid_locale" });

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
        return Results.Created("/internal/auth/me", new { id, email, input.DisplayName, input.Timezone, baseCurrency = input.BaseCurrency.ToUpperInvariant() });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        await transaction.RollbackAsync();
        return Results.Conflict(new { error = "email_exists" });
    }
});

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
});

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
});

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
});

app.MapGet("/internal/auth/me", async (ClaimsPrincipal principal, NpgsqlDataSource db) =>
{
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT id,email,display_name,timezone,base_currency,role,account_type,status,status_version FROM identity.users WHERE id=$1 AND status='active'");
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadUser(reader)) : Results.NotFound();
}).RequireAuthorization();

app.Run();

static AuthUser ReadUser(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8));

static async Task<object> CreateTokenPair(NpgsqlDataSource db, AuthUser user, Guid familyId, SecurityKey key, string issuer, string audience)
{
    var now = DateTime.UtcNow;
    var claims = new[] {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role), new Claim("account_type", user.AccountType), new Claim("status_version", user.StatusVersion.ToString()),
        new Claim("timezone", user.Timezone), new Claim("base_currency", user.BaseCurrency.Trim())
    };
    var jwt = new JwtSecurityToken(issuer, audience, claims, now, now.AddMinutes(15), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
    var refreshBytes = RandomNumberGenerator.GetBytes(32);
    await using var command = db.CreateCommand("INSERT INTO identity.refresh_tokens (id,user_id,family_id,token_hash,expires_at) VALUES ($1,$2,$3,$4,$5)");
    command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue(user.Id); command.Parameters.AddWithValue(familyId);
    command.Parameters.AddWithValue(SHA256.HashData(refreshBytes)); command.Parameters.AddWithValue(now.AddDays(30));
    await command.ExecuteNonQueryAsync();
    return new { accessToken = new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt = now.AddMinutes(15), refreshToken = Convert.ToBase64String(refreshBytes) };
}

record RegisterRequest(string Email, string Password, string DisplayName, string Timezone, string BaseCurrency);
record LoginRequest(string Email, string Password);
record RefreshRequest(string RefreshToken);
record AuthUser(Guid Id, string Email, string DisplayName, string Timezone, string BaseCurrency, string Role, string AccountType, string Status, int StatusVersion);
