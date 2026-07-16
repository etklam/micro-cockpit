using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class DiaryReviewItemsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    [Fact]
    public async Task Review_items_return_only_active_diaries_owned_by_the_authenticated_user()
    {
        await using var fixture = await ReviewItemsFixture.StartAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await fixture.AddDiary(owner, new DateOnly(2026, 7, 3), "Owner active");
        await fixture.AddDiary(owner, new DateOnly(2026, 7, 2), "Owner deleted", deleted: true);
        await fixture.AddDiary(other, new DateOnly(2026, 7, 1), "Other active");

        using var response = await fixture.Client(owner).GetAsync("/internal/diary-review-items?from=2026-07-01&to=2026-07-31");
        var body = await response.Content.ReadFromJsonAsync<DiaryReviewItemsResponse>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(body!.Items);
        Assert.Equal("Owner active", item.Title);
        Assert.Equal(DiaryReviewStatus.unreviewed, item.ReviewStatus);
    }

    [Fact]
    public async Task Review_items_apply_filters_and_cursor_pagination_without_duplicates()
    {
        await using var fixture = await ReviewItemsFixture.StartAsync();
        var owner = Guid.NewGuid();
        var good = await fixture.AddDiary(owner, new DateOnly(2026, 7, 4), "Good", createdAt: new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc));
        var poor = await fixture.AddDiary(owner, new DateOnly(2026, 7, 3), "Poor", createdAt: new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc));
        await fixture.AddDiary(owner, new DateOnly(2026, 7, 2), "Unreviewed", createdAt: new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc));
        await fixture.AddReview(good, owner, "good", "calm", ["no_plan"]);
        await fixture.AddReview(poor, owner, "poor", "anxious", ["fomo"]);
        using var client = fixture.Client(owner);

        Assert.Equal(["Good", "Poor"], (await client.GetFromJsonAsync<DiaryReviewItemsResponse>("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&status=reviewed", Json))!.Items.Select(item => item.Title));
        Assert.Equal("Unreviewed", Assert.Single((await client.GetFromJsonAsync<DiaryReviewItemsResponse>("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&status=unreviewed", Json))!.Items).Title);
        Assert.Equal("Good", Assert.Single((await client.GetFromJsonAsync<DiaryReviewItemsResponse>("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&assessment=good", Json))!.Items).Title);
        Assert.Equal("Poor", Assert.Single((await client.GetFromJsonAsync<DiaryReviewItemsResponse>("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&tag=fomo", Json))!.Items).Title);

        var first = await client.GetFromJsonAsync<DiaryReviewItemsResponse>("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&limit=2", Json);
        Assert.NotNull(first!.NextCursor); Assert.Equal(2, first.Items.Count);
        var second = await client.GetFromJsonAsync<DiaryReviewItemsResponse>($"/internal/diary-review-items?from=2026-07-01&to=2026-07-31&limit=2&cursor={Uri.EscapeDataString(first.NextCursor)}", Json);
        Assert.Single(second!.Items); Assert.Empty(first.Items.Select(item => item.DiaryId).Intersect(second.Items.Select(item => item.DiaryId)));

        var empty = await client.GetAsync("/internal/diary-review-items?from=2026-08-01&to=2026-08-31");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode); Assert.Empty((await empty.Content.ReadFromJsonAsync<DiaryReviewItemsResponse>(Json))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diary-review-items?from=2026-01-01&to=2026-03-04")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diary-review-items?from=2026-07-01&to=2026-07-31&cursor=malformed")).StatusCode);
    }

    private sealed class ReviewItemsFixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly NpgsqlConnection _setup;
        private readonly NpgsqlDataSource _dataSource;
        private readonly WebApplicationFactory<Program> _factory;

        private ReviewItemsFixture(PostgreSqlContainer postgres, NpgsqlConnection setup, NpgsqlDataSource dataSource, WebApplicationFactory<Program> factory)
        { _postgres = postgres; _setup = setup; _dataSource = dataSource; _factory = factory; }

        internal static async Task<ReviewItemsFixture> StartAsync()
        {
            var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync();
            var setup = new NpgsqlConnection(postgres.GetConnectionString());
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            await new NpgsqlCommand(File.ReadAllText(Path.Combine(root, "platform/postgres/migrations/0001_initial_journal_performance.sql")), setup).ExecuteNonQueryAsync();
            await new NpgsqlCommand(File.ReadAllText(Path.Combine(root, "platform/postgres/migrations/0014_structured_diary_review.sql")), setup).ExecuteNonQueryAsync();
            await new NpgsqlCommand(File.ReadAllText(Path.Combine(root, "platform/postgres/migrations/0015_diary_review_ownership.sql")), setup).ExecuteNonQueryAsync();
            var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>(); services.RemoveAll<IHostedService>(); services.AddSingleton(dataSource);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = ReviewItemsAuth.Scheme; options.DefaultChallengeScheme = ReviewItemsAuth.Scheme; })
                    .AddScheme<AuthenticationSchemeOptions, ReviewItemsAuth>(ReviewItemsAuth.Scheme, _ => { });
            }));
            return new ReviewItemsFixture(postgres, setup, dataSource, factory);
        }

        internal HttpClient Client(Guid userId)
        { var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString()); return client; }

        internal async Task<Guid> AddDiary(Guid userId, DateOnly date, string title, bool deleted = false, DateTime? createdAt = null)
        {
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand("INSERT INTO journal.diaries(id,user_id,local_date,title,content,created_at,deleted_at) VALUES($1,$2,$3,$4,$5,$6,$7)", _setup);
            command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(date); command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue($"Content for {title}"); command.Parameters.AddWithValue(createdAt ?? DateTime.UtcNow); command.Parameters.AddWithValue(deleted ? DateTime.UtcNow : DBNull.Value);
            await command.ExecuteNonQueryAsync(); return id;
        }

        internal async Task AddReview(Guid diaryId, Guid userId, string assessment, string emotion, string[] tags)
        {
            await using var command = new NpgsqlCommand("INSERT INTO journal.diary_reviews(diary_id,user_id,process_assessment,emotion,discipline_score,execution_score,mistake_tags,lesson,next_action) VALUES($1,$2,$3,$4,4,3,$5,'Lesson','Next')", _setup);
            command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(assessment); command.Parameters.AddWithValue(emotion); command.Parameters.AddWithValue(tags);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        { _factory.Dispose(); await _dataSource.DisposeAsync(); await _setup.DisposeAsync(); await _postgres.DisposeAsync(); }
    }

    private sealed class ReviewItemsAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "review-items-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim("account_type", "human")], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
