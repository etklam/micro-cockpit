using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class DiaryReviewApiTests
{
    [Fact]
    public async Task Review_api_is_owned_optional_validated_and_null_aware()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
        await setup.OpenAsync();
        var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
        await new NpgsqlCommand(File.ReadAllText(Path.Combine(root, "platform/postgres/migrations/0001_initial_journal_performance.sql")), setup).ExecuteNonQueryAsync();
        await new NpgsqlCommand(File.ReadAllText(Path.Combine(root, "platform/postgres/migrations/0014_structured_diary_review.sql")), setup).ExecuteNonQueryAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var active = Guid.NewGuid(); var deleted = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("INSERT INTO journal.diaries(id,user_id,local_date,title,content,deleted_at) VALUES($1,$2,'2026-07-01','Active','',null),($3,$2,'2026-07-02','Deleted','',now())", setup))
        { seed.Parameters.AddWithValue(active); seed.Parameters.AddWithValue(owner); seed.Parameters.AddWithValue(deleted); await seed.ExecuteNonQueryAsync(); }

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>(); services.RemoveAll<IHostedService>(); services.AddSingleton(dataSource);
            services.AddAuthentication(options => { options.DefaultAuthenticateScheme = TestAuth.Scheme; options.DefaultChallengeScheme = TestAuth.Scheme; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", owner.ToString());
        var write = new { thesis = "Demand", plannedAction = (string?)null, actualAction = (string?)null, emotion = "calm", disciplineScore = 4, executionScore = (int?)null, processAssessment = "good", mistakeTags = new[] { "no_plan" }, lesson = (string?)null, nextAction = (string?)null };

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/internal/diaries/{active}/review", write)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/internal/diaries/{active}/review", new { write.thesis, write.plannedAction, write.actualAction, write.emotion, disciplineScore = 5, write.executionScore, write.processAssessment, write.mistakeTags, write.lesson, write.nextAction })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/internal/diaries/{Guid.NewGuid()}/review")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/internal/diaries/{deleted}/review", write)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/internal/diaries/{active}/review", new { write.thesis, write.plannedAction, write.actualAction, emotion = "excited", disciplineScore = 6, write.executionScore, write.processAssessment, mistakeTags = new[] { "unknown", "unknown" }, write.lesson, write.nextAction })).StatusCode);

        using var otherClient = factory.CreateClient(); otherClient.DefaultRequestHeaders.Add("X-Test-User", other.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/internal/diaries/{active}/review")).StatusCode);
        var summary = await client.GetFromJsonAsync<DiaryReviewSummaryResponse>("/internal/diary-review-summary?from=2026-07-01&to=2026-07-30");
        Assert.NotNull(summary); Assert.Equal(1, summary.ReviewedCount); Assert.Equal(5m, summary.AverageDisciplineScore); Assert.Null(summary.AverageExecutionScore); Assert.Equal(1, summary.TopMistakeTags.Single().Count);
        var empty = await otherClient.GetFromJsonAsync<DiaryReviewSummaryResponse>("/internal/diary-review-summary?from=2026-07-01&to=2026-07-30");
        Assert.NotNull(empty); Assert.Equal(0, empty.ReviewedCount); Assert.Null(empty.AverageDisciplineScore); Assert.Null(empty.AverageExecutionScore);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/internal/diaries/{active}/review")).StatusCode);
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "review-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim("account_type", "human")], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }
}
