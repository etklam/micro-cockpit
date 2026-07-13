using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class RefreshTokenRotationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("identity_test")
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
                email text NOT NULL,
                display_name text NOT NULL,
                timezone text NOT NULL DEFAULT 'Asia/Taipei',
                base_currency char(3) NOT NULL DEFAULT 'USD',
                role text NOT NULL DEFAULT 'user',
                account_type text NOT NULL DEFAULT 'human',
                status text NOT NULL DEFAULT 'active',
                status_version integer NOT NULL DEFAULT 1
            );
            CREATE TABLE identity.refresh_tokens (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES identity.users(id),
                family_id uuid NOT NULL,
                token_hash bytea NOT NULL UNIQUE,
                expires_at timestamptz NOT NULL,
                used_at timestamptz,
                revoked_at timestamptz
            );
            """, connection);
        await command.ExecuteNonQueryAsync();

        factory = new IdentityTestFactory(postgres.GetConnectionString());
        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Refresh_rotation_is_atomic_and_replay_revokes_the_family()
    {
        var userId = Guid.NewGuid();
        var successfulFamilyId = Guid.NewGuid();
        var successfulToken = RandomNumberGenerator.GetBytes(32);
        await SeedRefreshTokenAsync(userId, successfulFamilyId, successfulToken);

        using (var response = await client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = Convert.ToBase64String(successfulToken)
        }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var successfulState = await ReadFamilyStateAsync(successfulFamilyId, successfulToken);
        Assert.NotNull(successfulState.UsedAt);
        Assert.Equal(2, successfulState.TokenCount);
        Assert.Equal(0, successfulState.RevokedCount);

        using (var replay = await client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = Convert.ToBase64String(successfulToken)
        }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        }

        var replayState = await ReadFamilyStateAsync(successfulFamilyId, successfulToken);
        Assert.Equal(2, replayState.TokenCount);
        Assert.Equal(2, replayState.RevokedCount);

        var failedFamilyId = Guid.NewGuid();
        var failedToken = RandomNumberGenerator.GetBytes(32);
        await SeedRefreshTokenAsync(userId, failedFamilyId, failedToken);
        await InstallSuccessorInsertFailureAsync(failedFamilyId);

        var successorInsertFailure = await Assert.ThrowsAsync<PostgresException>(() => client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = Convert.ToBase64String(failedToken)
        }));
        Assert.Equal("P0001", successorInsertFailure.SqlState);

        var failedState = await ReadFamilyStateAsync(failedFamilyId, failedToken);
        Assert.Null(failedState.UsedAt);
        Assert.Equal(1, failedState.TokenCount);
        Assert.Equal(0, failedState.RevokedCount);
    }

    [Fact]
    public async Task Invalid_and_expired_tokens_are_rejected_without_revoking_a_family()
    {
        using (var invalid = await client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = "not-a-refresh-token"
        }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }

        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var expiredToken = RandomNumberGenerator.GetBytes(32);
        await SeedRefreshTokenAsync(userId, familyId, expiredToken, DateTime.UtcNow.AddMinutes(-1));

        using (var expired = await client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = Convert.ToBase64String(expiredToken)
        }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }

        var state = await ReadFamilyStateAsync(familyId, expiredToken);
        Assert.Null(state.UsedAt);
        Assert.Equal(1, state.TokenCount);
        Assert.Equal(0, state.RevokedCount);
    }

    [Fact]
    public async Task Inactive_account_cannot_rotate_a_refresh_token()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var token = RandomNumberGenerator.GetBytes(32);
        await SeedRefreshTokenAsync(userId, familyId, token, status: "inactive");

        using (var response = await client!.PostAsJsonAsync("/internal/auth/refresh", new
        {
            RefreshToken = Convert.ToBase64String(token)
        }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var state = await ReadFamilyStateAsync(familyId, token);
        Assert.Null(state.UsedAt);
        Assert.Equal(1, state.TokenCount);
        Assert.Equal(0, state.RevokedCount);
    }

    private async Task SeedRefreshTokenAsync(
        Guid userId,
        Guid familyId,
        byte[] token,
        DateTime? expiresAt = null,
        string status = "active")
    {
        await using var connection = await OpenConnectionAsync();
        await using var user = new NpgsqlCommand("""
            INSERT INTO identity.users (id,email,display_name,status)
            VALUES ($1,$2,$3,$4)
            ON CONFLICT (id) DO UPDATE SET status=excluded.status
            """, connection);
        user.Parameters.AddWithValue(userId);
        user.Parameters.AddWithValue($"{userId}@example.test");
        user.Parameters.AddWithValue("Test User");
        user.Parameters.AddWithValue(status);
        await user.ExecuteNonQueryAsync();

        await using var tokenCommand = new NpgsqlCommand("""
            INSERT INTO identity.refresh_tokens (id,user_id,family_id,token_hash,expires_at)
            VALUES ($1,$2,$3,$4,$5)
            """, connection);
        tokenCommand.Parameters.AddWithValue(Guid.NewGuid());
        tokenCommand.Parameters.AddWithValue(userId);
        tokenCommand.Parameters.AddWithValue(familyId);
        tokenCommand.Parameters.AddWithValue(SHA256.HashData(token));
        tokenCommand.Parameters.AddWithValue(expiresAt ?? DateTime.UtcNow.AddHours(1));
        await tokenCommand.ExecuteNonQueryAsync();
    }

    private async Task InstallSuccessorInsertFailureAsync(Guid familyId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"""
            CREATE FUNCTION identity.fail_successor_insert() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.family_id = '{familyId}'::uuid THEN
                    RAISE EXCEPTION 'forced successor insert failure';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER fail_successor_insert
            BEFORE INSERT ON identity.refresh_tokens
            FOR EACH ROW EXECUTE FUNCTION identity.fail_successor_insert();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(DateTime? UsedAt, int TokenCount, int RevokedCount)> ReadFamilyStateAsync(Guid familyId, byte[] originalToken)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT t.used_at,
                   (SELECT count(*)::int FROM identity.refresh_tokens WHERE family_id=$1),
                   (SELECT count(*)::int FROM identity.refresh_tokens WHERE family_id=$1 AND revoked_at IS NOT NULL)
            FROM identity.refresh_tokens t
            WHERE t.token_hash=$2
            """, connection);
        command.Parameters.AddWithValue(familyId);
        command.Parameters.AddWithValue(SHA256.HashData(originalToken));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.GetInt32(1),
            reader.GetInt32(2));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private sealed class IdentityTestFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = connectionString
            }));
        }
    }
}
