using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class AccessGrantApiTests
{
    [Fact]
    public async Task Comparison_is_read_only_owner_attributed_and_limited_to_grant_scope()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var humanId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var noGrantAgentId = Guid.NewGuid();
        await using var fixture = await Fixture.StartAsync(now, humanId, agentId, noGrantAgentId);
        await fixture.SeedClosure(humanId, new DateOnly(2026, 7, 18), "AI", "human AI view");
        await fixture.SeedClosure(agentId, new DateOnly(2026, 7, 18), "AI", "agent AI view");
        await fixture.SeedClosure(agentId, new DateOnly(2026, 7, 17), "Rates", "agent private rates view");

        using var human = fixture.Client(humanId, "human");
        using var grant = await human.PostAsJsonAsync("/internal/access-grants", new
        {
            agentUserId = agentId,
            mode = "ongoing",
            from = "2026-07-01",
            to = "2026-07-31",
            subjectType = "theme",
            subject = "AI",
        });
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);

        using var comparisonResponse = await human.GetAsync(
            $"/internal/comparison?agentUserId={agentId}&from=2026-07-01&to=2026-07-31&subjectType=theme&subject=AI");
        Assert.True(comparisonResponse.IsSuccessStatusCode, await comparisonResponse.Content.ReadAsStringAsync());
        var comparison = await comparisonResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("human", comparison.GetProperty("human").GetProperty("ownerType").GetString());
        Assert.Equal(humanId, comparison.GetProperty("human").GetProperty("ownerId").GetGuid());
        Assert.Equal("agent", comparison.GetProperty("agent").GetProperty("ownerType").GetString());
        Assert.Equal(agentId, comparison.GetProperty("agent").GetProperty("ownerId").GetGuid());
        Assert.Contains("human AI view", comparison.GetProperty("human").GetRawText());
        Assert.Contains("agent AI view", comparison.GetProperty("agent").GetRawText());
        Assert.DoesNotContain("agent private rates view", comparison.GetRawText());
        Assert.True(comparison.GetProperty("difference").GetProperty("outcomeConsistent").GetBoolean());
        Assert.Equal(0, comparison.GetProperty("difference").GetProperty("confidenceDifference").GetInt32());

        var unavailable = await human.GetFromJsonAsync<JsonElement>(
            $"/internal/comparison?agentUserId={noGrantAgentId}&from=2026-07-01&to=2026-07-31&subjectType=theme&subject=AI");
        Assert.Equal("unavailable", unavailable.GetProperty("agent").GetProperty("availability").GetString());
        Assert.Equal(0, unavailable.GetProperty("agent").GetProperty("observations").GetArrayLength());

        var empty = await human.GetFromJsonAsync<JsonElement>(
            $"/internal/comparison?agentUserId={agentId}&from=2026-07-19&to=2026-07-19&subjectType=theme&subject=AI");
        Assert.Equal("empty", empty.GetProperty("human").GetProperty("availability").GetString());
        Assert.Equal("empty", empty.GetProperty("agent").GetProperty("availability").GetString());

        using var invalid = await human.GetAsync(
            $"/internal/comparison?agentUserId={agentId}&from=2026-07-01&to=2026-07-31");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Fixed_and_ongoing_grants_enforce_closure_scope_expiry_revocation_and_owner_isolation()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var fixedAgent = Guid.NewGuid();
        var ongoingAgent = Guid.NewGuid();
        await using var fixture = await Fixture.StartAsync(now, owner, fixedAgent, ongoingAgent);
        var fixedSeed = await fixture.SeedClosure(owner, new DateOnly(2026, 7, 16), "AI", "initial fixed");
        var ongoingSeed = await fixture.SeedClosure(owner, new DateOnly(2026, 7, 17), "Macro", "initial ongoing");
        var deletionSeed = await fixture.SeedClosure(owner, new DateOnly(2026, 7, 18), "Rates", "erase this closure");
        await fixture.SeedClosure(otherOwner, new DateOnly(2026, 7, 16), "AI", "private other owner");

        using var human = fixture.Client(owner, "human");
        Assert.Equal(HttpStatusCode.NoContent,
            (await human.DeleteAsync($"/internal/market-observations/{deletionSeed.ObservationId}")).StatusCode);
        Assert.True(await fixture.ClosureIsAbsent(deletionSeed));
        foreach (var id in new[]
                 {
                     deletionSeed.UpdateId, deletionSeed.ExpectationId, deletionSeed.ReviewId,
                     deletionSeed.DecisionId, deletionSeed.TradeId,
                 })
            Assert.True(await fixture.RecordOutboxIsContentFree(id));

        using var unmanaged = await human.PostAsJsonAsync("/internal/access-grants", new
        {
            agentUserId = Guid.NewGuid(), mode = "fixed", from = "2026-07-01", to = "2026-07-31",
        });
        Assert.Equal(HttpStatusCode.Forbidden, unmanaged.StatusCode);

        using var fixedCreate = await human.PostAsJsonAsync("/internal/access-grants", new
        {
            agentUserId = fixedAgent, mode = "fixed", from = "2026-07-01", to = "2026-07-31",
            subjectType = "theme", subject = "AI",
        });
        Assert.True(fixedCreate.StatusCode == HttpStatusCode.Created, await fixedCreate.Content.ReadAsStringAsync());
        var fixedGrant = await fixedCreate.Content.ReadFromJsonAsync<JsonElement>();
        var fixedGrantId = fixedGrant.GetProperty("id").GetGuid();

        await fixture.EditUpdate(fixedSeed.UpdateId, "edited fixed");
        var lateFixedChild = await fixture.AddUpdate(owner, fixedSeed.ObservationId, "late fixed child", "AI");
        using var fixedClient = fixture.Client(fixedAgent, "agent");
        var fixedPage = await fixedClient.GetFromJsonAsync<JsonElement>("/internal/agent/granted-records?tag=focus&reviewReadiness=reviewed&author=" + owner);
        var syncCursor = fixedPage.GetProperty("syncCursor").GetString()!;
        Assert.Equal(1, fixedPage.GetProperty("items").GetArrayLength());
        var fixedRecords = fixedPage.GetProperty("items")[0].GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(6, fixedRecords.Length);
        Assert.Contains(fixedRecords, record => record.GetProperty("recordType").GetString() == "observation_update"
            && record.GetProperty("content").GetProperty("content").GetString() == "edited fixed");
        Assert.DoesNotContain(fixedRecords, record => record.GetProperty("id").GetGuid() == lateFixedChild);
        Assert.DoesNotContain(fixedPage.GetRawText(), "private other owner");

        await fixture.EditUpdate(fixedSeed.UpdateId, "incremental edit");
        await fixture.DeleteTrade(fixedSeed.TradeId);
        var changes = await fixedClient.GetFromJsonAsync<JsonElement>(
            "/internal/agent/journal-changes?cursor=" + Uri.EscapeDataString(syncCursor));
        Assert.Contains(changes.GetProperty("items").EnumerateArray(), change =>
            change.GetProperty("recordId").GetGuid() == fixedSeed.UpdateId
            && change.GetProperty("content").GetProperty("content").GetString() == "incremental edit");
        var deletion = changes.GetProperty("items").EnumerateArray().Single(change =>
            change.GetProperty("recordId").GetGuid() == fixedSeed.TradeId);
        Assert.Equal(new[] { "recordId", "recordType", "deletedAt" },
            deletion.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("trade", deletion.GetProperty("recordType").GetString());
        Assert.True(await fixture.RecordOutboxIsContentFree(fixedSeed.TradeId));

        using var stolenEdit = await fixedClient.PutAsJsonAsync($"/internal/observation-updates/{fixedSeed.UpdateId}", new { content = "stolen" });
        Assert.Equal(HttpStatusCode.NotFound, stolenEdit.StatusCode);
        using var ownCreate = await PostQuick(fixedClient, "agent-owned observation", "agent-own", "Hermes delegate");
        Assert.Equal(HttpStatusCode.Created, ownCreate.StatusCode);
        var ownUpdate = (await ownCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        Assert.Equal("Hermes delegate", await fixture.SourceLabel(ownUpdate));
        Assert.Equal(HttpStatusCode.OK, (await fixedClient.PutAsJsonAsync($"/internal/observation-updates/{ownUpdate}", new { content = "agent edit" })).StatusCode);

        using var ongoingCreate = await human.PostAsJsonAsync("/internal/access-grants", new
        {
            agentUserId = ongoingAgent, mode = "ongoing", from = "2026-07-01", to = "2026-07-31",
            subjectType = "theme", subject = "Macro", expiresAt = "2026-07-21T12:00:00Z",
        });
        Assert.True(ongoingCreate.StatusCode == HttpStatusCode.Created, await ongoingCreate.Content.ReadAsStringAsync());
        var ongoingGrantId = (await ongoingCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var lateOngoingChild = await fixture.AddUpdate(owner, ongoingSeed.ObservationId, "late ongoing child", "Macro");
        using var ongoingClient = fixture.Client(ongoingAgent, "agent");
        var pageOne = await ongoingClient.GetFromJsonAsync<JsonElement>("/internal/agent/granted-records?from=2026-07-17&to=2026-07-17&subjectType=theme&subject=Macro&limit=1");
        Assert.Contains(pageOne.GetProperty("items")[0].GetProperty("records").EnumerateArray(),
            record => record.GetProperty("id").GetGuid() == lateOngoingChild);
        Assert.Equal(JsonValueKind.Null, pageOne.GetProperty("nextCursor").ValueKind);
        Assert.Equal(0, (await ongoingClient.GetFromJsonAsync<JsonElement>("/internal/agent/granted-records?instrumentId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")).GetProperty("items").GetArrayLength());

        fixture.SetNow(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, (await ongoingClient.GetFromJsonAsync<JsonElement>("/internal/agent/granted-records")).GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await human.DeleteAsync($"/internal/access-grants/{fixedGrantId}")).StatusCode);
        Assert.Equal(0, (await fixedClient.GetFromJsonAsync<JsonElement>("/internal/agent/granted-records")).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client(otherOwner, "human").DeleteAsync($"/internal/access-grants/{ongoingGrantId}")).StatusCode);

        fixture.SetNow(new DateTimeOffset(2026, 10, 21, 12, 0, 1, TimeSpan.Zero));
        using var expired = await fixedClient.GetAsync("/internal/agent/journal-changes?cursor=" + Uri.EscapeDataString(syncCursor));
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Contains("cursor_expired_fresh_sync_required", await expired.Content.ReadAsStringAsync());

        using var exportResponse = await human.GetAsync("/internal/account-export");
        Assert.True(exportResponse.IsSuccessStatusCode, await exportResponse.Content.ReadAsStringAsync());
        var export = await exportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("incremental edit", export.GetRawText());
        Assert.Contains("initial ongoing", export.GetRawText());
        Assert.DoesNotContain("erase this closure", export.GetRawText());
        Assert.DoesNotContain("private other owner", export.GetRawText());
        using var deletionResponse = await human.DeleteAsync("/internal/account-data");
        Assert.True(deletionResponse.StatusCode == HttpStatusCode.NoContent, await deletionResponse.Content.ReadAsStringAsync());
        Assert.True(await fixture.OwnedContentIsAbsent(owner, fixedAgent, ongoingAgent));
        Assert.Equal("private other owner", await fixture.UpdateContentForOwner(otherOwner));
    }

    private static async Task<HttpResponseMessage> PostQuick(HttpClient client, string content, string key, string? sourceLabel = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/quick-observations")
        {
            Content = JsonContent.Create(new { content, sourceLabel }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer postgres;
        private readonly NpgsqlDataSource db;
        private readonly WebApplicationFactory<Program> factory;
        private readonly MutableTimeProvider clock;

        private Fixture(PostgreSqlContainer postgres, NpgsqlDataSource db, WebApplicationFactory<Program> factory, MutableTimeProvider clock)
        {
            this.postgres = postgres;
            this.db = db;
            this.factory = factory;
            this.clock = clock;
        }

        internal static async Task<Fixture> StartAsync(DateTimeOffset now, Guid manager, params Guid[] agents)
        {
            var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync();
            await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[]
            {
                "0001_initial_journal_performance.sql", "0003_extend_transactions.sql",
                "0013_journal_idempotency.sql",
                "0014_structured_diary_review.sql", "0015_diary_review_ownership.sql",
                "0018_diary_tags_and_list_indexes.sql",
                "0026_market_observations.sql", "0028_observation_enrichment.sql", "0029_expectations.sql",
                "0030_expectation_reviews.sql", "0031_action_decisions_trades.sql", "0032_watchlist.sql",
                "0033_pattern_review_discipline_principles.sql", "0036_agent_access_grants.sql",
                "0037_incremental_record_changes.sql", "0042_observation_search_indexes.sql",
            })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();

            var db = NpgsqlDataSource.Create(postgres.GetConnectionString());
            var clock = new MutableTimeProvider(now);
            var allowed = agents.ToHashSet();
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(db);
                services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient("identity").ConfigurePrimaryHttpMessageHandler(() => new ManagedAgentHandler(manager, allowed));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuth.Scheme;
                    options.DefaultChallengeScheme = TestAuth.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
            })).WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ServiceKey"] = "test-service-key" })));
            return new Fixture(postgres, db, factory, clock);
        }

        internal HttpClient Client(Guid id, string accountType)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", id.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Account", accountType);
            return client;
        }

        internal async Task<SeedIds> SeedClosure(Guid owner, DateOnly day, string subject, string content)
        {
            var observation = Guid.NewGuid();
            var update = Guid.NewGuid();
            var expectation = Guid.NewGuid();
            var review = Guid.NewGuid();
            var decision = Guid.NewGuid();
            var trade = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                WITH observation AS (
                    INSERT INTO journal.market_observations(id,user_id,journal_day,timezone,rollover_time)
                    VALUES($1,$2,$3,'UTC','00:00') RETURNING id
                ), update_record AS (
                    INSERT INTO journal.observation_updates(id,market_observation_id,user_id,content,recorded_at,tags,primary_subject)
                    VALUES($4,$1,$2,$5,$6,ARRAY['focus'],$7::jsonb) RETURNING id
                ), expectation AS (
                    INSERT INTO journal.expectations(id,observation_update_id,user_id,expected_behavior,deadline,invalidation_condition,confidence,market)
                    VALUES($8,$4,$2,'Expected behavior',$9,'Invalidation','medium','US') RETURNING id
                ), review AS (
                    INSERT INTO journal.expectation_reviews(id,expectation_id,user_id,outcome,reasoning_quality)
                    VALUES($10,$8,$2,'confirmed','sound') RETURNING id
                ), decision AS (
                    INSERT INTO journal.action_decisions(id,observation_update_id,expectation_id,user_id,intent,reason)
                    VALUES($11,$4,$8,$2,'trade','Decision reason') RETURNING id
                )
                INSERT INTO journal.trades(id,action_decision_id,user_id,symbol,side,quantity,price,currency,executed_at)
                VALUES($12,$11,$2,'AAPL','buy',1,100,'USD',$6)
                """);
            command.Parameters.AddWithValue(observation);
            command.Parameters.AddWithValue(owner);
            command.Parameters.AddWithValue(day);
            command.Parameters.AddWithValue(update);
            command.Parameters.AddWithValue(content);
            command.Parameters.AddWithValue(clock.GetUtcNow().UtcDateTime);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(new { type = "theme", name = subject }));
            command.Parameters.AddWithValue(expectation);
            command.Parameters.AddWithValue(clock.GetUtcNow().AddHours(-1).UtcDateTime);
            command.Parameters.AddWithValue(review);
            command.Parameters.AddWithValue(decision);
            command.Parameters.AddWithValue(trade);
            await command.ExecuteNonQueryAsync();
            return new(observation, update, expectation, review, decision, trade);
        }

        internal async Task<Guid> AddUpdate(Guid owner, Guid observation, string content, string subject)
        {
            var id = Guid.NewGuid();
            await using var command = db.CreateCommand("""
                INSERT INTO journal.observation_updates(id,market_observation_id,user_id,content,recorded_at,tags,primary_subject)
                VALUES($1,$2,$3,$4,$5,ARRAY['focus'],$6::jsonb)
                """);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(observation);
            command.Parameters.AddWithValue(owner);
            command.Parameters.AddWithValue(content);
            command.Parameters.AddWithValue(clock.GetUtcNow().UtcDateTime);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(new { type = "theme", name = subject }));
            await command.ExecuteNonQueryAsync();
            return id;
        }

        internal async Task EditUpdate(Guid id, string content)
        {
            await using var command = db.CreateCommand("UPDATE journal.observation_updates SET content=$2,updated_at=now() WHERE id=$1");
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(content);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task DeleteTrade(Guid id)
        {
            await using var command = db.CreateCommand("UPDATE journal.trades SET deleted_at=now(),updated_at=now() WHERE id=$1");
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<bool> RecordOutboxIsContentFree(Guid id)
        {
            await using var command = db.CreateCommand("""
                SELECT count(*)=1
                       AND NOT EXISTS (
                           SELECT 1 FROM information_schema.columns
                           WHERE table_schema='journal'
                             AND table_name IN ('record_changes','record_outbox_events')
                             AND column_name IN ('payload','content')
                       )
                FROM journal.record_outbox_events
                WHERE record_id=$1 AND operation='deleted'
                """);
            command.Parameters.AddWithValue(id);
            return (bool)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<string?> SourceLabel(Guid updateId)
        {
            await using var command = db.CreateCommand("SELECT source_label FROM journal.observation_updates WHERE id=$1");
            command.Parameters.AddWithValue(updateId);
            return (string?)await command.ExecuteScalarAsync();
        }

        internal async Task<bool> OwnedContentIsAbsent(params Guid[] ownerIds)
        {
            await using var command = db.CreateCommand("""
                SELECT
                    NOT EXISTS (SELECT 1 FROM journal.market_observations WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.observation_updates WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.expectations WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.expectation_reviews WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.action_decisions WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.trades WHERE user_id=ANY($1))
                    AND NOT EXISTS (SELECT 1 FROM journal.agent_access_grants WHERE owner_user_id=ANY($1) OR agent_user_id=ANY($1))
                """);
            command.Parameters.AddWithValue(ownerIds);
            return (bool)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<bool> ClosureIsAbsent(SeedIds ids)
        {
            await using var command = db.CreateCommand("""
                SELECT
                    NOT EXISTS (SELECT 1 FROM journal.market_observations WHERE id=$1)
                    AND NOT EXISTS (SELECT 1 FROM journal.observation_updates WHERE id=$2)
                    AND NOT EXISTS (SELECT 1 FROM journal.expectations WHERE id=$3)
                    AND NOT EXISTS (SELECT 1 FROM journal.expectation_reviews WHERE id=$4)
                    AND NOT EXISTS (SELECT 1 FROM journal.action_decisions WHERE id=$5)
                    AND NOT EXISTS (SELECT 1 FROM journal.trades WHERE id=$6)
                """);
            command.Parameters.AddWithValue(ids.ObservationId);
            command.Parameters.AddWithValue(ids.UpdateId);
            command.Parameters.AddWithValue(ids.ExpectationId);
            command.Parameters.AddWithValue(ids.ReviewId);
            command.Parameters.AddWithValue(ids.DecisionId);
            command.Parameters.AddWithValue(ids.TradeId);
            return (bool)(await command.ExecuteScalarAsync())!;
        }

        internal async Task<string?> UpdateContentForOwner(Guid ownerId)
        {
            await using var command = db.CreateCommand("SELECT content FROM journal.observation_updates WHERE user_id=$1");
            command.Parameters.AddWithValue(ownerId);
            return (string?)await command.ExecuteScalarAsync();
        }

        internal void SetNow(DateTimeOffset now) => clock.Set(now);

        public async ValueTask DisposeAsync()
        {
            factory.Dispose();
            await db.DisposeAsync();
            await postgres.DisposeAsync();
        }
    }

    private sealed class ManagedAgentHandler(Guid manager, HashSet<Guid> agents) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/internal/auth/agents")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        items = agents.Select(id => new { userId = id }).ToArray(),
                    }),
                });
            var parts = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var managed = parts.Length >= 6
                && Guid.TryParse(parts[3], out var agent)
                && Guid.TryParse(parts[5], out var owner)
                && owner == manager && agents.Contains(agent);
            return Task.FromResult(new HttpResponseMessage(managed ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        internal void Set(DateTimeOffset value) => current = value;
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "access-grant-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var account = Request.Headers["X-Test-Account"].FirstOrDefault() ?? "human";
            var claims = new List<Claim>
            {
                new("sub", id),
                new("account_type", account),
                new("timezone", "UTC"),
                new("journal_day_rollover", "00:00"),
            };
            if (account == "agent")
            {
                claims.Add(new("scope", "journal:read"));
                claims.Add(new("scope", "journal:write"));
                claims.Add(new("scope", "agent:read"));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }

    private sealed record SeedIds(
        Guid ObservationId,
        Guid UpdateId,
        Guid ExpectationId,
        Guid ReviewId,
        Guid DecisionId,
        Guid TradeId);
}
