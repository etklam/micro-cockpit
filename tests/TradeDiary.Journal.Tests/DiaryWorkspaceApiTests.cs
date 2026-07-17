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

public sealed class DiaryWorkspaceApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task List_filters_pagination_and_tags_cover_workspace_contract()
    {
        await using var fixture = await WorkspaceFixture.StartAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var stamp = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

        var a = await fixture.AddDiary(owner, new DateOnly(2026, 7, 10), "Alpha FOMO", "Bought the breakout", stamp);
        var b = await fixture.AddDiary(owner, new DateOnly(2026, 7, 10), "Beta Plan", "Stuck to plan", stamp.AddSeconds(1));
        var c = await fixture.AddDiary(owner, new DateOnly(2026, 7, 9), "Gamma", "plain content %_\\ wild", stamp);
        await fixture.AddDiary(owner, new DateOnly(2026, 7, 8), "Deleted", "gone", stamp, deleted: true);
        await fixture.AddDiary(other, new DateOnly(2026, 7, 10), "Other user", "secret", stamp);
        await fixture.AddReview(a, owner);
        await fixture.AddTags(a, owner, ["fomo", "breakout"]);
        await fixture.AddTags(b, owner, ["discipline"]);
        await fixture.AddTransaction(a, owner, "AAPL");
        await fixture.AddTransaction(a, owner, "AAPL"); // duplicate symbol must not multiply diary rows
        await fixture.AddTransaction(b, owner, "MSFT", deleted: true);

        using var client = fixture.Client(owner);

        // User isolation
        var all = await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?limit=100", Json);
        Assert.Equal(3, all!.Items.Count);
        Assert.DoesNotContain(all.Items, item => item.Title.Contains("Other", StringComparison.Ordinal));

        // Keyword title / content / case-insensitive / literal wildcards
        Assert.Equal("Alpha FOMO", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?query=fomo", Json))!.Items).Title);
        Assert.Equal("Alpha FOMO", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?query=BREAKOUT", Json))!.Items).Title);
        Assert.Equal("Gamma", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?query=%25_%5C", Json))!.Items).Title);

        // Date inclusive
        Assert.Equal(2, (await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?from=2026-07-10&to=2026-07-10", Json))!.Items.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diaries?from=2026-07-11&to=2026-07-10")).StatusCode);

        // Review status
        Assert.Equal("Alpha FOMO", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?reviewStatus=reviewed", Json))!.Items).Title);
        Assert.Equal(2, (await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?reviewStatus=unreviewed", Json))!.Items.Count);

        // Symbol exact + deleted tx ignored + no duplicates
        var symbol = await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?symbol=aapl", Json);
        Assert.Equal("Alpha FOMO", Assert.Single(symbol!.Items).Title);
        Assert.Empty((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?symbol=MSFT", Json))!.Items);

        // Tag exact
        Assert.Equal("Alpha FOMO", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?tag=FOMO", Json))!.Items).Title);
        // Blank tag is absent (not invalid)
        Assert.Equal(3, (await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?tag=", Json))!.Items.Count);
        Assert.Equal(3, (await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?tag=%20", Json))!.Items.Count);

        // Combined AND
        Assert.Equal("Alpha FOMO", Assert.Single((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?query=fomo&symbol=AAPL&tag=breakout&reviewStatus=reviewed", Json))!.Items).Title);
        Assert.Empty((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?query=fomo&symbol=MSFT", Json))!.Items);

        // Stable pagination with equal local dates / timestamps order by id
        var first = await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?limit=1", Json);
        Assert.NotNull(first!.NextCursor);
        Assert.Single(first.Items);
        var second = await client.GetFromJsonAsync<DiaryPage>($"/internal/diaries?limit=1&cursor={Uri.EscapeDataString(first.NextCursor)}", Json);
        Assert.Single(second!.Items);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        var third = await client.GetFromJsonAsync<DiaryPage>($"/internal/diaries?limit=1&cursor={Uri.EscapeDataString(second.NextCursor!)}", Json);
        Assert.Single(third!.Items);
        Assert.Null(third.NextCursor);
        var ids = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToArray();
        Assert.Equal(3, ids.Distinct().Count());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diaries?cursor=not-a-cursor")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diaries?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/internal/diaries?reviewStatus=maybe")).StatusCode);

        // Create stores normalized tags, collapses duplicates, rejects >10 and invalid without partial write
        using var create = new HttpRequestMessage(HttpMethod.Post, "/internal/diaries")
        {
            Content = JsonContent.Create(new { localDate = "2026-07-11", title = "Tagged", content = "body", tags = new[] { " FOMO ", "fomo", "Plan" } })
        };
        create.Headers.Add("Idempotency-Key", "create-tags-1");
        using var createdResponse = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<DiaryResponse>(Json);
        Assert.Equal(["fomo", "plan"], created!.Tags);

        using var createReplay = new HttpRequestMessage(HttpMethod.Post, "/internal/diaries")
        {
            Content = JsonContent.Create(new { localDate = "2026-07-11", title = "Tagged", content = "body", tags = new[] { " FOMO ", "fomo", "Plan" } })
        };
        createReplay.Headers.Add("Idempotency-Key", "create-tags-1");
        using var replayResponse = await client.SendAsync(createReplay);
        var replay = await replayResponse.Content.ReadFromJsonAsync<DiaryResponse>(Json);
        Assert.Equal(created.Id, replay!.Id);
        Assert.Equal(created.Tags, replay.Tags);

        var tooMany = Enumerable.Range(1, 11).Select(i => $"tag{i}").ToArray();
        using var tooManyRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/diaries")
        {
            Content = JsonContent.Create(new { localDate = "2026-07-11", title = "Too many", content = "x", tags = tooMany })
        };
        tooManyRequest.Headers.Add("Idempotency-Key", "create-tags-2");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(tooManyRequest)).StatusCode);

        // Update replaces tags atomically; invalid tag does not partially update
        await client.PutAsJsonAsync($"/internal/diaries/{created.Id}", new { localDate = "2026-07-11", title = "Tagged", content = "body", tags = new[] { "calm" } });
        var afterValid = await client.GetFromJsonAsync<DiaryResponse>($"/internal/diaries/{created.Id}", Json);
        Assert.Equal(["calm"], afterValid!.Tags);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/internal/diaries/{created.Id}", new { localDate = "2026-07-11", title = "Changed", content = "new", tags = new[] { "bad\ntag" } })).StatusCode);
        var afterInvalid = await client.GetFromJsonAsync<DiaryResponse>($"/internal/diaries/{created.Id}", Json);
        Assert.Equal("Tagged", afterInvalid!.Title);
        Assert.Equal(["calm"], afterInvalid.Tags);

        // Cross-user tag probing returns no data
        using var otherClient = fixture.Client(other);
        Assert.Empty((await otherClient.GetFromJsonAsync<DiaryPage>("/internal/diaries?tag=fomo", Json))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/internal/diaries/{a}")).StatusCode);

        // Deleted diary tags unavailable
        await client.DeleteAsync($"/internal/diaries/{created.Id}");
        Assert.Empty((await client.GetFromJsonAsync<DiaryPage>("/internal/diaries?tag=calm", Json))!.Items);
    }

    private sealed class WorkspaceFixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly NpgsqlConnection _setup;
        private readonly NpgsqlDataSource _dataSource;
        private readonly WebApplicationFactory<Program> _factory;

        private WorkspaceFixture(PostgreSqlContainer postgres, NpgsqlConnection setup, NpgsqlDataSource dataSource, WebApplicationFactory<Program> factory)
        {
            _postgres = postgres; _setup = setup; _dataSource = dataSource; _factory = factory;
        }

        internal static async Task<WorkspaceFixture> StartAsync()
        {
            var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync();
            var setup = new NpgsqlConnection(postgres.GetConnectionString());
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[]
                     {
                         "0001_initial_journal_performance.sql",
                         "0003_extend_transactions.sql",
                         "0005_reminder.sql",
                         "0006_diary_deleted_events.sql",
                         "0013_journal_idempotency.sql",
                         "0014_structured_diary_review.sql",
                         "0015_diary_review_ownership.sql",
                         "0018_diary_tags_and_list_indexes.sql",
                     })
            {
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();
            }

            var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton(dataSource);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = WorkspaceAuth.Scheme;
                    options.DefaultChallengeScheme = WorkspaceAuth.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, WorkspaceAuth>(WorkspaceAuth.Scheme, _ => { });
            }));
            return new WorkspaceFixture(postgres, setup, dataSource, factory);
        }

        internal HttpClient Client(Guid userId)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            return client;
        }

        internal async Task<Guid> AddDiary(Guid userId, DateOnly date, string title, string content, DateTime createdAt, bool deleted = false)
        {
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand(
                "INSERT INTO journal.diaries(id,user_id,local_date,title,content,created_at,updated_at,deleted_at) VALUES($1,$2,$3,$4,$5,$6,$6,$7)", _setup);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(date);
            command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue(content);
            command.Parameters.AddWithValue(createdAt);
            command.Parameters.AddWithValue(deleted ? createdAt : DBNull.Value);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        internal async Task AddReview(Guid diaryId, Guid userId)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO journal.diary_reviews(diary_id,user_id,process_assessment,emotion,discipline_score,execution_score,mistake_tags) VALUES($1,$2,'mixed','calm',3,3,'{}')", _setup);
            command.Parameters.AddWithValue(diaryId);
            command.Parameters.AddWithValue(userId);
            await command.ExecuteNonQueryAsync();
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

        internal async Task AddTransaction(Guid diaryId, Guid userId, string symbol, bool deleted = false)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO journal.transactions(id,diary_id,user_id,symbol,side,quantity,price,currency,traded_at,notes,deleted_at)
                VALUES($1,$2,$3,$4,'buy',1,1,'USD',now(),'',$5)
                """, _setup);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(diaryId);
            command.Parameters.AddWithValue(userId);
            command.Parameters.AddWithValue(symbol.ToUpperInvariant());
            command.Parameters.AddWithValue(deleted ? DateTime.UtcNow : DBNull.Value);
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

    private sealed class WorkspaceAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "workspace-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim("account_type", "human")], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
