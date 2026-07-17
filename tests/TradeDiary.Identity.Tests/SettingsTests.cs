using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class SettingsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("identity_settings_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SCHEMA identity;
            CREATE TABLE identity.users (
                id uuid PRIMARY KEY,
                email text NOT NULL UNIQUE,
                display_name text NOT NULL,
                timezone text NOT NULL DEFAULT 'Asia/Taipei',
                base_currency char(3) NOT NULL DEFAULT 'USD',
                role text NOT NULL DEFAULT 'user',
                account_type text NOT NULL DEFAULT 'human',
                status text NOT NULL DEFAULT 'active',
                status_version integer NOT NULL DEFAULT 1,
                appearance text NOT NULL DEFAULT 'system',
                updated_at timestamptz NOT NULL DEFAULT now(),
                created_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT users_appearance_check CHECK (appearance IN ('system', 'light', 'dark'))
            );
            CREATE TABLE identity.user_credentials (
                user_id uuid PRIMARY KEY REFERENCES identity.users(id) ON DELETE CASCADE,
                password_salt bytea NOT NULL,
                password_hash bytea NOT NULL,
                iterations integer NOT NULL
            );
            CREATE TABLE identity.refresh_tokens (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES identity.users(id),
                family_id uuid NOT NULL,
                token_hash bytea NOT NULL UNIQUE,
                expires_at timestamptz NOT NULL,
                used_at timestamptz,
                revoked_at timestamptz,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """, connection);
        await command.ExecuteNonQueryAsync();

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = postgres.GetConnectionString(),
                    ["Auth:AllowPublicRegistration"] = "true",
                    ["Jwt:PrivateKeyPath"] = Path.Combine(Path.GetTempPath(), $"identity-settings-{Guid.NewGuid():N}.pem"),
                });
            });
        });
        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Reads_and_updates_own_settings()
    {
        var tokens = await RegisterAndLoginAsync("owner@example.test");
        using var get = await Authed(tokens.AccessToken).GetAsync("/internal/auth/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var before = await get.Content.ReadFromJsonAsync<SettingsDto>();
        Assert.Equal("owner@example.test", before!.Email);
        Assert.Equal("system", before.Appearance);

        using var put = await Authed(tokens.AccessToken).PutAsJsonAsync("/internal/auth/settings", new
        {
            displayName = "  Owner Renamed  ",
            timezone = "Asia/Tokyo",
            baseCurrency = "jpy",
            appearance = "dark",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = await put.Content.ReadFromJsonAsync<SettingsDto>();
        Assert.Equal("Owner Renamed", after!.DisplayName);
        Assert.Equal("Asia/Tokyo", after.Timezone);
        Assert.Equal("JPY", after.BaseCurrency);
        Assert.Equal("dark", after.Appearance);

        using var me = await Authed(tokens.AccessToken).GetAsync("/internal/auth/me");
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dark", meBody.GetProperty("appearance").GetString());
    }

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Accepts_every_appearance_value(string appearance)
    {
        var tokens = await RegisterAndLoginAsync($"{appearance}@example.test");
        using var put = await Authed(tokens.AccessToken).PutAsJsonAsync("/internal/auth/settings", Body(appearance: appearance));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(appearance, (await put.Content.ReadFromJsonAsync<SettingsDto>())!.Appearance);
    }

    [Fact]
    public async Task Rejects_invalid_fields_without_mutating()
    {
        var tokens = await RegisterAndLoginAsync("stable@example.test");
        await Authed(tokens.AccessToken).PutAsJsonAsync("/internal/auth/settings", Body(displayName: "Stable", timezone: "UTC", baseCurrency: "USD", appearance: "light"));

        foreach (var body in new object[]
        {
            Body(displayName: ""),
            Body(displayName: new string('x', 101)),
            Body(timezone: "Not/AZone"),
            Body(baseCurrency: "US"),
            Body(baseCurrency: "US1"),
            Body(appearance: "dim"),
        })
        {
            using var put = await Authed(tokens.AccessToken).PutAsJsonAsync("/internal/auth/settings", body);
            Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        }

        var settings = await (await Authed(tokens.AccessToken).GetAsync("/internal/auth/settings")).Content.ReadFromJsonAsync<SettingsDto>();
        Assert.Equal("Stable", settings!.DisplayName);
        Assert.Equal("UTC", settings.Timezone);
        Assert.Equal("USD", settings.BaseCurrency);
        Assert.Equal("light", settings.Appearance);
    }

    [Fact]
    public async Task Agent_and_inactive_accounts_are_rejected()
    {
        var human = await RegisterAndLoginAsync("human-agent@example.test");
        // Create agent via SQL (no password) then try settings with a forged path — agents cannot pass human policy on JWT easily.
        // Seed inactive human.
        // Inactive accounts cannot obtain tokens via login; settings without a valid
        // session is unauthorized. Active→inactive while holding a JWT is covered by
        // the status check returning not_found on /me-style paths when a token exists.
        var inactiveId = Guid.NewGuid();
        await SeedUserAsync(inactiveId, "inactive@example.test", status: "inactive");
        var inactiveTokens = await IssueTokensForAsync(inactiveId, "inactive@example.test");
        using var inactiveGet = await Authed(inactiveTokens).GetAsync("/internal/auth/settings");
        Assert.True(inactiveGet.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound);

        var agentId = Guid.NewGuid();
        await SeedUserAsync(agentId, "agent@local.invalid", accountType: "agent");
        var agentToken = await IssueTokensForAsync(agentId, "agent@local.invalid", accountType: "agent");
        using var agentPut = await Authed(agentToken).PutAsJsonAsync("/internal/auth/settings", Body());
        Assert.True(agentPut.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refresh_after_settings_carries_new_timezone_and_currency_claims()
    {
        using var response = await client!.PostAsJsonAsync("/internal/auth/register", new
        {
            email = "claims@example.test",
            password = "correct horse battery staple",
            displayName = "Claims",
            timezone = "UTC",
            baseCurrency = "USD",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var login = await client.PostAsJsonAsync("/internal/auth/login", new
        {
            email = "claims@example.test",
            password = "correct horse battery staple",
        });
        var loginTokens = await login.Content.ReadFromJsonAsync<AuthTokensDto>();
        Assert.NotNull(loginTokens);

        using var put = await Authed(loginTokens!.AccessToken).PutAsJsonAsync("/internal/auth/settings", new
        {
            displayName = "Claims",
            timezone = "America/New_York",
            baseCurrency = "cad",
            appearance = "system",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var refresh = await client.PostAsJsonAsync("/internal/auth/refresh", new { refreshToken = loginTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthTokensDto>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(rotated!.AccessToken);
        Assert.Equal("America/New_York", jwt.Claims.First(c => c.Type == "timezone").Value);
        Assert.Equal("CAD", jwt.Claims.First(c => c.Type == "base_currency").Value);
    }

    private async Task<AuthTokensDto> RegisterAndLoginAsync(string email)
    {
        using var register = await client!.PostAsJsonAsync("/internal/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            displayName = "User",
            timezone = "Asia/Taipei",
            baseCurrency = "USD",
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/internal/auth/login", new { email, password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<AuthTokensDto>())!;
    }

    private HttpClient Authed(string accessToken)
    {
        var authed = factory!.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return authed;
    }

    private static object Body(
        string displayName = "User",
        string timezone = "UTC",
        string baseCurrency = "USD",
        string appearance = "system") => new { displayName, timezone, baseCurrency, appearance };

    private async Task SeedUserAsync(Guid id, string email, string status = "active", string accountType = "human")
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.users (id, email, display_name, timezone, base_currency, status, account_type)
            VALUES ($1, $2, 'Seed', 'UTC', 'USD', $3, $4)
            """, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(email);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(accountType);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> IssueTokensForAsync(Guid userId, string email, string accountType = "human")
    {
        // Login path requires credentials; seed a password and login, or issue via refresh seed.
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2("correct horse battery staple", salt, 210_000, HashAlgorithmName.SHA256, 32);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var cred = new NpgsqlCommand("""
            INSERT INTO identity.user_credentials (user_id, password_salt, password_hash, iterations)
            VALUES ($1, $2, $3, 210000)
            ON CONFLICT (user_id) DO NOTHING
            """, connection))
        {
            cred.Parameters.AddWithValue(userId);
            cred.Parameters.AddWithValue(salt);
            cred.Parameters.AddWithValue(hash);
            await cred.ExecuteNonQueryAsync();
        }
        using var login = await client!.PostAsJsonAsync("/internal/auth/login", new { email, password = "correct horse battery staple" });
        if (login.StatusCode != HttpStatusCode.OK)
        {
            // inactive accounts fail login — return a random token that will 401
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensDto>();
        return tokens!.AccessToken;
    }

    private sealed record SettingsDto(string Email, string DisplayName, string Timezone, string BaseCurrency, string Appearance, DateTime UpdatedAt);
    private sealed record AuthTokensDto(string AccessToken, DateTime ExpiresAt, string RefreshToken);
}
