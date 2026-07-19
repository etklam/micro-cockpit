using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class PartnerDiaryApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Partner_diary_read_enforces_authz_and_strips_private_fields()
    {
        await using var fixture = await PartnerDiaryFixture.StartAsync(allowed: true);
        var owner = Guid.NewGuid();
        var partner = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var day = new DateOnly(2026, 7, 10);
        var diaryId = await fixture.AddDiary(owner, day, "Shared day", "Body with **markdown**");
        await fixture.AddTags(diaryId, owner, ["focus", "plan"]);
        await fixture.AddTransaction(diaryId, owner, "AAPL");
        await fixture.AddReview(diaryId, owner);
        await fixture.AddDiary(owner, day, "Second entry", "Same day second");

        using var partnerClient = fixture.Client(partner);
        using var outsiderClient = fixture.Client(outsider);
        using var ownerClient = fixture.Client(owner);

        // Owner cannot use partner path against self.
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerClient.GetAsync($"/internal/partner-diaries?ownerId={owner}&from=2026-07-01&to=2026-07-31")).StatusCode);

        // Range validation (max 366 days inclusive => DayNumber delta > 366 fails).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await partnerClient.GetAsync($"/internal/partner-diaries?ownerId={owner}&from=2025-01-01&to=2026-07-10")).StatusCode);

        using var allowed = await partnerClient.GetAsync($"/internal/partner-diaries?ownerId={owner}&from=2026-07-01&to=2026-07-31");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadFromJsonAsync<CollectionResponse<PartnerDiaryItem>>(Json);
        Assert.Equal(2, body!.Items.Count);
        Assert.All(body.Items, item =>
        {
            Assert.Equal(day, item.LocalDate);
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Content));
        });
        Assert.Contains(body.Items, x => x.Tags.Contains("focus"));
        // Multiple entries on one date remain separate.
        Assert.Equal(2, body.Items.Count(x => x.LocalDate == day));
        // No private fields in JSON.
        var raw = await allowed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("transaction", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thesis", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disciplineScore", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", raw, StringComparison.OrdinalIgnoreCase);

        // Unauthorized outsider still gets non-disclosing 404 when Partner says no.
        await using var deniedFixture = await PartnerDiaryFixture.StartAsync(allowed: false);
        using var deniedClient = deniedFixture.Client(outsider);
        await deniedFixture.AddDiary(owner, day, "Secret", "nope");
        Assert.Equal(HttpStatusCode.NotFound,
            (await deniedClient.GetAsync($"/internal/partner-diaries?ownerId={owner}&from=2026-07-01&to=2026-07-31")).StatusCode);
    }

    private sealed record CollectionResponse<T>(List<T> Items);
    private sealed record PartnerDiaryItem(Guid Id, DateOnly LocalDate, string Title, string Content, List<string> Tags);

    private sealed class PartnerDiaryFixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        private NpgsqlConnection _setup = null!;
        private NpgsqlDataSource _dataSource = null!;
        private WebApplicationFactory<Program> _factory = null!;
        private readonly bool _allowed;

        private PartnerDiaryFixture(bool allowed) => _allowed = allowed;

        internal static async Task<PartnerDiaryFixture> StartAsync(bool allowed)
        {
            var fixture = new PartnerDiaryFixture(allowed);
            await fixture._postgres.StartAsync();
            fixture._setup = new NpgsqlConnection(fixture._postgres.GetConnectionString());
            await fixture._setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[]
                     {
                         "0001_initial_journal_performance.sql",
                         "0003_extend_transactions.sql",
                         "0013_journal_idempotency.sql",
                         "0014_structured_diary_review.sql",
                         "0015_diary_review_ownership.sql",
                         "0018_diary_tags_and_list_indexes.sql"
                     })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), fixture._setup)
                    .ExecuteNonQueryAsync();

            fixture._dataSource = NpgsqlDataSource.Create(fixture._postgres.GetConnectionString());
            fixture._factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton(fixture._dataSource);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuth.Scheme;
                        options.DefaultChallengeScheme = TestAuth.Scheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                    services.AddHttpClient("partner").ConfigurePrimaryHttpMessageHandler(() =>
                        new StubPartnerHandler(fixture._allowed));
                });
            });
            return fixture;
        }

        internal HttpClient Client(Guid userId)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            return client;
        }

        internal async Task<Guid> AddDiary(Guid userId, DateOnly date, string title, string content)
        {
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand(
                "INSERT INTO journal.diaries(id,user_id,local_date,title,content) VALUES($1,$2,$3,$4,$5)", _setup);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(date);
            command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue(content);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        internal async Task AddTags(Guid diaryId, Guid userId, string[] tags)
        {
            foreach (var tag in tags)
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO journal.diary_tags(diary_id,user_id,tag) VALUES($1,$2,$3)", _setup);
                command.Parameters.AddWithValue(diaryId);
                command.Parameters.AddWithValue(userId);
                command.Parameters.AddWithValue(tag.ToLowerInvariant());
                await command.ExecuteNonQueryAsync();
            }
        }

        internal async Task AddTransaction(Guid diaryId, Guid userId, string symbol)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO journal.transactions(id,diary_id,user_id,symbol,side,quantity,price,currency,traded_at,notes)
                VALUES($1,$2,$3,$4,'buy',1,1,'USD',now(),'')
                """, _setup);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(diaryId);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(symbol);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task AddReview(Guid diaryId, Guid userId)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO journal.diary_reviews(diary_id,user_id,thesis,process_assessment,emotion,discipline_score,execution_score,mistake_tags) VALUES($1,$2,'private thesis','good','calm',5,5,'{}')",
                _setup);
            command.Parameters.AddWithValue(diaryId);
            command.Parameters.AddWithValue(userId);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _factory.Dispose();
            await _dataSource.DisposeAsync();
            await _setup.DisposeAsync();
            await _postgres.DisposeAsync();
        }
    }

    private sealed class StubPartnerHandler(bool allowed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = $"{{\"allowed\":{(allowed ? "true" : "false")}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "partner-diary-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, id),
                new Claim("account_type", "human")
            ], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
