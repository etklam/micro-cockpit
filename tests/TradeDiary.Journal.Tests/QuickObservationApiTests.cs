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
            foreach (var file in new[] { "0001_initial_journal_performance.sql", "0013_journal_idempotency.sql", "0026_market_observations.sql" })
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

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
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
