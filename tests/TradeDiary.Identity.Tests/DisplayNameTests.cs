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

public sealed class DisplayNameTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("identity_display_name_test")
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
                timezone text NOT NULL DEFAULT 'UTC',
                base_currency char(3) NOT NULL DEFAULT 'USD',
                role text NOT NULL DEFAULT 'user',
                account_type text NOT NULL DEFAULT 'human',
                status text NOT NULL DEFAULT 'active',
                status_version integer NOT NULL DEFAULT 1,
                appearance text NOT NULL DEFAULT 'system',
                locale text NOT NULL DEFAULT 'en',
                updated_at timestamptz NOT NULL DEFAULT now(),
                created_at timestamptz NOT NULL DEFAULT now()
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
                    ["Jwt:PrivateKeyPath"] = Path.Combine(Path.GetTempPath(), $"identity-display-{Guid.NewGuid():N}.pem"),
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
    public async Task Display_names_return_null_for_blank_and_omit_inactive()
    {
        var viewerId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var blankId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        await SeedUserAsync(viewerId, "viewer@example.test", "Viewer");
        await SeedUserAsync(activeId, "active@example.test", "Active Name");
        await SeedUserAsync(blankId, "blank@example.test", "   ");
        await SeedUserAsync(inactiveId, "gone@example.test", "Gone", status: "disabled");

        var token = await IssueTokenAsync(viewerId, "viewer@example.test");
        using var response = await Authed(token).GetAsync(
            $"/internal/users/display-names?ids={activeId:D},{blankId:D},{inactiveId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var active = items.Single(x => x.GetProperty("userId").GetGuid() == activeId);
        Assert.Equal("Active Name", active.GetProperty("displayName").GetString());
        var blank = items.Single(x => x.GetProperty("userId").GetGuid() == blankId);
        Assert.Equal(JsonValueKind.Null, blank.GetProperty("displayName").ValueKind);
        Assert.DoesNotContain(items, x => x.GetProperty("userId").GetGuid() == inactiveId);
    }

    private async Task SeedUserAsync(Guid id, string email, string displayName, string status = "active")
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO identity.users (id, email, display_name, timezone, base_currency, status, account_type)
            VALUES ($1, $2, $3, 'UTC', 'USD', $4, 'human')
            """, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(email);
        command.Parameters.AddWithValue(displayName);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> IssueTokenAsync(Guid userId, string email)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2("correct horse battery staple", salt, 210_000, HashAlgorithmName.SHA256, 32);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var cred = new NpgsqlCommand("""
            INSERT INTO identity.user_credentials (user_id, password_salt, password_hash, iterations)
            VALUES ($1, $2, $3, 210000)
            ON CONFLICT (user_id) DO NOTHING
            """, connection);
        cred.Parameters.AddWithValue(userId);
        cred.Parameters.AddWithValue(salt);
        cred.Parameters.AddWithValue(hash);
        await cred.ExecuteNonQueryAsync();
        using var login = await client!.PostAsJsonAsync("/internal/auth/login", new { email, password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokensDto>();
        return tokens!.AccessToken;
    }

    private HttpClient Authed(string accessToken)
    {
        var authed = factory!.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return authed;
    }

    private sealed record AuthTokensDto(string AccessToken, DateTime ExpiresAt, string RefreshToken);
}
