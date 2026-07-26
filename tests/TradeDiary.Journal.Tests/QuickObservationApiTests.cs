using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class QuickObservationApiTests
{
    private static readonly Guid KnownInstrumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData("2026-07-14T01:29:59Z", "America/Los_Angeles", "18:30", "2026-07-12")]
    [InlineData("2026-07-14T01:30:00Z", "America/Los_Angeles", "18:30", "2026-07-13")]
    [InlineData("2026-03-08T09:30:00Z", "America/Los_Angeles", "02:00", "2026-03-07")]
    [InlineData("2026-03-08T10:00:00Z", "America/Los_Angeles", "02:00", "2026-03-08")]
    [InlineData("2026-11-01T08:29:59Z", "America/Los_Angeles", "01:30", "2026-10-31")]
    [InlineData("2026-11-01T08:30:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    [InlineData("2026-11-01T09:00:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    public void Journal_day_uses_timezone_and_rollover(string instant, string timezone, string rollover, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), JournalDay.Resolve(DateTimeOffset.Parse(instant), timezone, rollover));
    }

    [Fact]
    public async Task Quick_observation_creates_appends_and_allows_owner_edit()
    {
        var now = new DateTimeOffset(2026, 7, 14, 1, 30, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner, "America/Los_Angeles", "18:30");

        using var first = await Post(client, "first signal", "quick-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-07-13", firstBody.GetProperty("journalDay").GetString());
        Assert.False(firstBody.GetProperty("appended").GetBoolean());
        var observationId = firstBody.GetProperty("marketObservationId").GetGuid();
        var updateId = firstBody.GetProperty("observationUpdateId").GetGuid();

        using var replay = await Post(client, "first signal", "quick-1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(updateId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid());

        using var second = await Post(client, "changed view", "quick-2");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("appended").GetBoolean());
        Assert.Equal(observationId, secondBody.GetProperty("marketObservationId").GetGuid());

        var today = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        Assert.Equal(observationId, today.GetProperty("id").GetGuid());
        Assert.Equal(2, today.GetProperty("updates").GetArrayLength());
        Assert.Equal("first signal", today.GetProperty("updates")[0].GetProperty("content").GetString());
        Assert.Equal("changed view", today.GetProperty("updates")[1].GetProperty("content").GetString());

        using var edit = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "corrected signal" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var editBody = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(editBody.GetProperty("honestyReminderRequired").GetBoolean());

        var after = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        var edited = after.GetProperty("updates").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == updateId);
        Assert.Equal("corrected signal", edited.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Owner_can_enrich_an_update_without_blurring_subjects_tags_or_evidence()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var created = await Post(client, "Initial note", "enrich-1");
        var updateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        var instrumentId = KnownInstrumentId;

        using var enriched = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            signal = "Breadth improved into the close",
            interpretation = "Risk appetite may be returning",
            mentalState = "patient",
            tags = new[] { "Closing session", "closing session", "breadth" },
            primarySubject = new { type = "instrument", instrumentId, market = "US", symbol = "AAPL", displayName = "Apple Inc." },
            relatedSubjects = new object[]
            {
                new { type = "broad_market", name = "US equities" },
                new { type = "theme", name = "Artificial intelligence" },
                new { type = "instrument", market = "HK", symbol = "0700", displayName = "Tencent" },
            },
            evidence = new { url = "https://example.com/market", title = "Closing breadth", quote = "Advancers led decliners." },
        });

        Assert.Equal(HttpStatusCode.OK, enriched.StatusCode);
        var today = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations/today");
        var update = today.GetProperty("updates")[0];
        Assert.Equal("Breadth improved into the close", update.GetProperty("signal").GetString());
        Assert.Equal("Risk appetite may be returning", update.GetProperty("interpretation").GetString());
        Assert.Equal("patient", update.GetProperty("mentalState").GetString());
        Assert.Equal(new[] { "breadth", "closing session" }, update.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(instrumentId, update.GetProperty("primarySubject").GetProperty("instrumentId").GetGuid());
        Assert.True(update.GetProperty("primarySubject").GetProperty("dailyCloseAvailable").GetBoolean());
        Assert.False(update.GetProperty("relatedSubjects")[2].GetProperty("dailyCloseAvailable").GetBoolean());
        Assert.Equal("https://example.com/market", update.GetProperty("evidence").GetProperty("url").GetString());

        using var unsupportedUs = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            primarySubject = new { type = "instrument", market = "US", symbol = "AAPL", displayName = "Apple Inc." },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedUs.StatusCode);
        Assert.Contains("directory_instrument_required", await unsupportedUs.Content.ReadAsStringAsync());

        using var unknownInstrument = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            primarySubject = new { type = "instrument", instrumentId = Guid.NewGuid(), market = "US", symbol = "FAKE", displayName = "Fabricated" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownInstrument.StatusCode);
        Assert.Contains("unknown_instrument", await unknownInstrument.Content.ReadAsStringAsync());

        using var evidenceWithoutSignal = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            evidence = new { url = "https://example.com/source", title = "Source", quote = "Context" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, evidenceWithoutSignal.StatusCode);
        Assert.Contains("evidence_requires_signal", await evidenceWithoutSignal.Content.ReadAsStringAsync());

        using var invalidEvidence = await client.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new
        {
            content = "Initial note",
            signal = "A signal",
            evidence = new { url = "file:///tmp/source", title = "Local", quote = "Not allowed" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEvidence.StatusCode);
        Assert.Contains("invalid_evidence_url", await invalidEvidence.Content.ReadAsStringAsync());

        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "stolen", signal = "private" })).StatusCode);
    }

    [Fact]
    public async Task Owner_can_search_current_observation_updates_with_filters_and_stable_pagination()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        using var first = await Post(client, "Semiconductor breadth improved", "search-1");
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var enrich = await client.PutAsJsonAsync($"/internal/observation-updates/{firstId}", new
        {
            content = "Semiconductor breadth improved",
            signal = "Advancers led decliners",
            tags = new[] { "breadth" },
            primarySubject = new { type = "instrument", instrumentId = KnownInstrumentId, market = "US", symbol = "AAPL", displayName = "Apple" },
            relatedSubjects = new object[] { new { type = "theme", name = "AI" }, new { type = "instrument", market = "HK", symbol = "0700", displayName = "Tencent" } },
        });
        Assert.Equal(HttpStatusCode.OK, enrich.StatusCode);

        fixture.SetNow(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        using var second = await Post(client, "Dollar strengthened", "search-2");
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var secondEnrich = await client.PutAsJsonAsync($"/internal/observation-updates/{secondId}", new
        {
            content = "Dollar strengthened",
            primarySubject = new { type = "broad_market", name = "US macro" },
            tags = new[] { "closing session" },
        });
        Assert.Equal(HttpStatusCode.OK, secondEnrich.StatusCode);

        using var other = fixture.Client(Guid.NewGuid());
        using var privateWrite = await Post(other, "Private author note", "search-private");
        Assert.Equal(HttpStatusCode.Created, privateWrite.StatusCode);

        var filtered = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?from=2026-07-16&to=2026-07-16&query=semiconductor&subjectType=theme&subject=AI&instrumentId={KnownInstrumentId}&tag=breadth&author=current&limit=10");
        Assert.Equal(1, filtered.GetProperty("items").GetArrayLength());
        Assert.Equal(firstId, filtered.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        Assert.Equal(owner, filtered.GetProperty("items")[0].GetProperty("authorId").GetGuid());

        var page1 = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations?limit=1");
        Assert.Equal(secondId, page1.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var cursor = Uri.EscapeDataString(page1.GetProperty("nextCursor").GetString()!);
        var page2 = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?limit=1&cursor={cursor}");
        Assert.Equal(firstId, page2.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextCursor").ValueKind);

        var byInstrument = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?instrumentId={KnownInstrumentId}&author={owner}");
        Assert.Equal(firstId, byInstrument.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var manualInstrument = await client.GetFromJsonAsync<JsonElement>("/internal/market-observations?market=HK&symbol=0700");
        Assert.Equal(firstId, manualInstrument.GetProperty("items")[0].GetProperty("update").GetProperty("id").GetGuid());
        var hidden = await client.GetFromJsonAsync<JsonElement>($"/internal/market-observations?author={Guid.NewGuid()}");
        Assert.Equal(0, hidden.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/market-observations?cursor=invalid")).StatusCode);

        var days = await client.GetFromJsonAsync<JsonElement>("/internal/market-observation-day-summary?from=2026-07-01&to=2026-07-31");
        Assert.Equal(2, days.GetProperty("items").GetArrayLength());
        Assert.Equal(1, days.GetProperty("items")[0].GetProperty("updateCount").GetInt64());
        Assert.Equal(0, days.GetProperty("items")[0].GetProperty("readyForReviewCount").GetInt64());
    }

    [Fact]
    public async Task Owner_can_optionally_create_an_active_expectation_from_an_observation_update()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/observation-updates/{updateId}/expectations")
        {
            Content = JsonContent.Create(new
            {
                expectedBehavior = "Breadth should remain above 60%",
                deadline = "2026-07-17T20:00:00Z",
                invalidationCondition = "Breadth closes below 45%",
                confidence = "medium",
                market = "US",
            }),
        };
        request.Headers.Add("Idempotency-Key", "expectation-1");
        using var created = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(updateId, body.GetProperty("observationUpdateId").GetGuid());
        Assert.Equal("Breadth should remain above 60%", body.GetProperty("expectedBehavior").GetString());
        Assert.Equal("medium", body.GetProperty("confidence").GetString());
        Assert.Equal("active", body.GetProperty("readiness").GetString());
        Assert.False(body.GetProperty("deadlineElapsed").GetBoolean());

        var expectations = await client.GetFromJsonAsync<JsonElement>($"/internal/expectations?observationUpdateId={updateId}");
        Assert.Single(expectations.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Expectation_creation_rejects_unsupported_market_trading_day_preset_and_hides_cross_owner_access()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent-2");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var unsupportedMarket = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Nikkei should hold support",
            deadlinePreset = "next_trading_day",
            invalidationCondition = "Closes below support",
            confidence = "low",
            market = "JP",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedMarket.StatusCode);
        Assert.Contains("trading_day_preset_unavailable", await unsupportedMarket.Content.ReadAsStringAsync());

        using var other = fixture.Client(Guid.NewGuid());
        using var stolen = await other.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "stolen",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "n/a",
            confidence = "low",
            market = "US",
        });
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Owner_can_invalidate_an_expectation_early_and_editing_an_elapsed_deadline_shows_honesty_reminder()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = await Fixture.StartAsync(now);
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);
        using var observation = await Post(client, "Breadth is improving", "expectation-parent-3");
        var updateId = (await observation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();

        using var created = await client.PostAsJsonAsync($"/internal/observation-updates/{updateId}/expectations", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-17T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "medium",
            market = "US",
        });
        var expectationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var invalidated = await client.PostAsync($"/internal/expectations/{expectationId}/invalidate", null);
        Assert.Equal(HttpStatusCode.OK, invalidated.StatusCode);
        var invalidatedBody = await invalidated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready_for_review", invalidatedBody.GetProperty("readiness").GetString());
        Assert.NotEqual(JsonValueKind.Null, invalidatedBody.GetProperty("invalidatedAt").ValueKind);

        fixture.SetNow(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        using var edit = await client.PutAsJsonAsync($"/internal/expectations/{expectationId}", new
        {
            expectedBehavior = "Breadth should remain above 60%",
            deadline = "2026-07-21T20:00:00Z",
            invalidationCondition = "Breadth closes below 45%",
            confidence = "high",
            market = "US",
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var editBody = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(editBody.GetProperty("honestyReminderRequired").GetBoolean());
        Assert.Equal("high", editBody.GetProperty("confidence").GetString());

        var days = await client.GetFromJsonAsync<JsonElement>("/internal/market-observation-day-summary?from=2026-07-14&to=2026-07-14");
        Assert.Equal(1, days.GetProperty("items")[0].GetProperty("readyForReviewCount").GetInt64());
    }

    [Fact]
    public async Task Blank_capture_creates_nothing_and_cross_owner_edit_is_hidden()
    {
        await using var fixture = await Fixture.StartAsync(new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));
        var owner = Guid.NewGuid();
        using var client = fixture.Client(owner);

        using var blank = await Post(client, "   ", "blank-1");
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(0, await fixture.Count("journal.market_observations"));
        Assert.Equal(0, await fixture.Count("journal.observation_updates"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/market-observations/today")).StatusCode);

        using var created = await Post(client, "private", "private-1");
        var updateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("observationUpdateId").GetGuid();
        using var other = fixture.Client(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/internal/observation-updates/{updateId}", new { content = "stolen" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/internal/market-observations/today")).StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string content, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/quick-observations") { Content = JsonContent.Create(new { content }) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer postgres;
        private readonly NpgsqlDataSource dataSource;
        private readonly WebApplicationFactory<Program> factory;
        private readonly FixedTimeProvider clock;

        private Fixture(PostgreSqlContainer postgres, NpgsqlDataSource dataSource, WebApplicationFactory<Program> factory, FixedTimeProvider clock)
        {
            this.postgres = postgres;
            this.dataSource = dataSource;
            this.factory = factory;
            this.clock = clock;
        }

        internal static async Task<Fixture> StartAsync(DateTimeOffset now)
        {
            var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync();
            await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[] { "0001_initial_journal_performance.sql", "0013_journal_idempotency.sql", "0026_market_observations.sql", "0028_observation_enrichment.sql", "0029_expectations.sql" })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();

            var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
            var clock = new FixedTimeProvider(now);
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(dataSource);
                services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient("market-data").ConfigurePrimaryHttpMessageHandler(() => new InstrumentDirectoryHandler());
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuth.Scheme;
                    options.DefaultChallengeScheme = TestAuth.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
            }));
            return new Fixture(postgres, dataSource, factory, clock);
        }

        internal HttpClient Client(Guid userId, string timezone = "UTC", string rollover = "00:00")
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Timezone", timezone);
            client.DefaultRequestHeaders.Add("X-Test-Rollover", rollover);
            return client;
        }

        internal void SetNow(DateTimeOffset value) => clock.Set(value);

        internal async Task<long> Count(string table)
        {
            await using var command = dataSource.CreateCommand($"SELECT count(*) FROM {table}");
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            factory.Dispose();
            await dataSource.DisposeAsync();
            await postgres.DisposeAsync();
        }
    }

    private sealed class InstrumentDirectoryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var known = request.RequestUri?.AbsolutePath.EndsWith(KnownInstrumentId.ToString(), StringComparison.OrdinalIgnoreCase) is true;
            return Task.FromResult(known
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { instrumentId = KnownInstrumentId, symbol = "AAPL", name = "Apple Inc.", exchange = "NASDAQ", currency = "USD", timezone = "America/New_York" }) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset current = value;
        internal void Set(DateTimeOffset next) => current = next;
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "quick-observation-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("sub", id),
                new Claim("account_type", "human"),
                new Claim("timezone", Request.Headers["X-Test-Timezone"].FirstOrDefault() ?? "UTC"),
                new Claim("journal_day_rollover", Request.Headers["X-Test-Rollover"].FirstOrDefault() ?? "00:00"),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
