using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class AgentTokenTests
{
    [Fact]
    public async Task Managing_human_can_rotate_revoke_and_observe_one_nonexpiring_agent_token()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        await using (var setup = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[] { "0002_identity.sql", "0012_identity_api_keys.sql", "0019_identity_user_preferences.sql", "0020_identity_user_locale.sql", "0023_identity_user_accent_theme.sql", "0025_identity_journal_day_rollover.sql", "0035_agent_users_tokens.sql" })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();
        }
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = postgres.GetConnectionString(),
                ["Auth:AllowPublicRegistration"] = "true",
                ["Internal:ServiceKey"] = "test-service-key",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(dataSource);
            });
        });
        using var client = factory.CreateClient();
        var ownerToken = await RegisterAndLogin(client, "owner-agent@example.test");
        var intruderToken = await RegisterAndLogin(client, "intruder-agent@example.test");

        using var provision = await Authed(client, ownerToken).PostAsJsonAsync("/internal/auth/agents", new
        {
            name = "Research agent",
            displayName = "Research Agent",
            timezone = "UTC",
            baseCurrency = "USD",
            scopes = new[] { "journal:read", "journal:write", "agent:read" },
            expiresAt = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);
        var created = await provision.Content.ReadFromJsonAsync<JsonElement>();
        var agentId = created.GetProperty("userId").GetGuid();
        var firstToken = created.GetProperty("apiToken").GetString()!;
        Assert.NotEqual(Guid.Empty, agentId);
        Assert.Equal(true, await Scalar(dataSource, "SELECT expires_at IS NULL FROM identity.api_keys WHERE user_id=$1 AND revoked_at IS NULL", agentId));
        Assert.Equal(1L, await Scalar(dataSource, "SELECT count(*) FROM identity.api_keys WHERE user_id=$1 AND revoked_at IS NULL", agentId));
        using (var managed = new HttpRequestMessage(HttpMethod.Get, $"/internal/auth/agents/{agentId}/managed-by/{created.GetProperty("userId").GetGuid()}"))
        {
            managed.Headers.Add("X-Service-Key", "test-service-key");
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(managed)).StatusCode);
        }
        var ownerId = (Guid)(await Scalar(dataSource, "SELECT manager_user_id FROM identity.agent_managers WHERE agent_user_id=$1", agentId))!;
        using (var managed = new HttpRequestMessage(HttpMethod.Get, $"/internal/auth/agents/{agentId}/managed-by/{ownerId}"))
        {
            managed.Headers.Add("X-Service-Key", "test-service-key");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(managed)).StatusCode);
        }

        var initialList = await Authed(client, ownerToken).GetFromJsonAsync<JsonElement>("/internal/auth/agents");
        var initial = initialList.GetProperty("items")[0];
        Assert.NotEqual(JsonValueKind.Null, initial.GetProperty("tokenCreatedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("lastUsedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("lastSuccessfulRequestAt").ValueKind);
        Assert.Empty((await Authed(client, intruderToken).GetFromJsonAsync<JsonElement>("/internal/auth/agents")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Authed(client, intruderToken).PostAsJsonAsync($"/internal/auth/agents/{agentId}/token", new { scopes = new[] { "journal:read" } })).StatusCode);

        using var exchange = await client.PostAsJsonAsync("/internal/auth/api-key/token", new { apiKey = firstToken });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var agentAccess = (await exchange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        Assert.NotEqual(ownerToken, agentAccess);
        var used = (await Authed(client, ownerToken).GetFromJsonAsync<JsonElement>("/internal/auth/agents")).GetProperty("items")[0];
        Assert.NotEqual(JsonValueKind.Null, used.GetProperty("lastUsedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, used.GetProperty("lastSuccessfulRequestAt").ValueKind);

        using var rotated = await Authed(client, ownerToken).PostAsJsonAsync($"/internal/auth/agents/{agentId}/token", new { scopes = new[] { "journal:read", "agent:read" } });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var secondToken = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiToken").GetString()!;
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(1L, await Scalar(dataSource, "SELECT count(*) FROM identity.api_keys WHERE user_id=$1 AND revoked_at IS NULL", agentId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/auth/api-key/token", new { apiKey = firstToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/internal/auth/api-key/token", new { apiKey = secondToken })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Authed(client, ownerToken).DeleteAsync($"/internal/auth/agents/{agentId}/token")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/auth/api-key/token", new { apiKey = secondToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/auth/api-key/token", new { apiKey = "not-base64" })).StatusCode);

        var export = await Authed(client, ownerToken).GetFromJsonAsync<JsonElement>("/internal/auth/account-export");
        Assert.Equal(ownerId, export.GetProperty("profile").GetProperty("id").GetGuid());
        Assert.Equal(agentId, export.GetProperty("agents")[0].GetProperty("userId").GetGuid());
        Assert.DoesNotContain("keyHash", export.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await Authed(client, ownerToken).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/internal/auth/account")
            {
                Content = JsonContent.Create(new { confirmation = "NO" }),
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Authed(client, ownerToken).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/internal/auth/account")
            {
                Content = JsonContent.Create(new { confirmation = "DELETE" }),
            })).StatusCode);
        Assert.Equal(0L, await Scalar(dataSource, "SELECT count(*) FROM identity.users WHERE id=ANY($1)", new[] { ownerId, agentId }));
        Assert.Equal(1L, await Scalar(dataSource, "SELECT count(*) FROM identity.users WHERE email='intruder-agent@example.test'"));

        var platformAgent = Guid.NewGuid();
        await using (var user = dataSource.CreateCommand("INSERT INTO identity.users(id,email,display_name,account_type) VALUES($1,$2,'Official Agent','agent')"))
        {
            user.Parameters.AddWithValue(platformAgent);
            user.Parameters.AddWithValue($"agent-{platformAgent:N}@local.invalid");
            await user.ExecuteNonQueryAsync();
        }
        await using (var manager = dataSource.CreateCommand("INSERT INTO identity.agent_managers(agent_user_id,manager_type,manager_user_id) VALUES($1,'platform',NULL)"))
        {
            manager.Parameters.AddWithValue(platformAgent);
            await manager.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> RegisterAndLogin(HttpClient client, string email)
    {
        using var register = await client.PostAsJsonAsync("/internal/auth/register", new
        {
            email, password = "Correct horse battery staple 123!", displayName = email.Split('@')[0], timezone = "UTC", baseCurrency = "USD",
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await client.PostAsJsonAsync("/internal/auth/login", new { email, password = "Correct horse battery staple 123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static HttpClient Authed(HttpClient source, string token)
    {
        source.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return source;
    }

    private static async Task<object?> Scalar(NpgsqlDataSource db, string sql, Guid id)
    {
        await using var command = db.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<object?> Scalar(NpgsqlDataSource db, string sql, Guid[] ids)
    {
        await using var command = db.CreateCommand(sql);
        command.Parameters.AddWithValue(ids);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<object?> Scalar(NpgsqlDataSource db, string sql)
    {
        await using var command = db.CreateCommand(sql);
        return await command.ExecuteScalarAsync();
    }
}
