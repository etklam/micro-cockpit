using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public sealed class CockpitReadModelsTests
{
    [Fact]
    public async Task Dashboard_returns_503_when_required_dependency_fails()
    {
        using var factory = CreateFactory((service, path) =>
        {
            if (service == "performance" && path.StartsWith("/internal/performance/day/", StringComparison.Ordinal))
                return Json(HttpStatusCode.InternalServerError, "{}");
            return DashboardResponse(service, path);
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/app/dashboard");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_degrades_when_optional_dependencies_fail()
    {
        using var factory = CreateFactory((service, path) =>
        {
            if (service is "discipline" or "reminder")
                return Json(HttpStatusCode.InternalServerError, "{}");
            return DashboardResponse(service, path);
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/app/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unavailable", root.GetProperty("capabilities").GetProperty("alerts").GetString());
        Assert.Equal("unavailable", root.GetProperty("capabilities").GetProperty("discipline").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("pendingAlerts").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("discipline").ValueKind);
    }

    [Fact]
    public async Task Calendar_merges_journal_performance_and_alert_facts_by_date()
    {
        using var factory = CreateFactory(CalendarResponse);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/app/calendar?year=2026&month=7");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var days = root.GetProperty("days");
        var first = days.EnumerateArray().Single(day => day.GetProperty("date").GetString() == "2026-07-01");
        var second = days.EnumerateArray().Single(day => day.GetProperty("date").GetString() == "2026-07-02");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(31, days.GetArrayLength());
        Assert.Equal(2, first.GetProperty("diaryCount").GetInt64());
        Assert.Equal(3, first.GetProperty("transactionCount").GetInt64());
        Assert.Equal(7, first.GetProperty("alertCount").GetInt64());
        Assert.Equal(12.5m, first.GetProperty("performance").GetProperty("pnlAmount").GetDecimal());
        Assert.Equal(0, second.GetProperty("diaryCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("performance").ValueKind);
        Assert.Equal(0, second.GetProperty("alertCount").GetInt64());
    }

    [Fact]
    public void ResolveLocalDate_uses_timezone_claim_at_utc_boundary()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("timezone", "America/Los_Angeles")]));
        var utc = new DateTimeOffset(2026, 7, 14, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 7, 13), CockpitReadModels.ResolveLocalDate(user, utc));
    }

    private static WebApplicationFactory<Program> CreateFactory(Func<string, string, HttpResponseMessage> responder)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });

                foreach (var service in new[] { "journal", "performance", "discipline", "reminder" })
                {
                    services.AddHttpClient(service)
                        .ConfigurePrimaryHttpMessageHandler(() => new DownstreamHandler(service, responder));
                }
            }));
    }

    private static HttpResponseMessage DashboardResponse(string service, string path)
    {
        var date = QueryValue(path, "date") ?? QueryValue(path, "from") ?? path[(path.LastIndexOf('/') + 1)..];
        return service switch
        {
            "journal" when path.Contains("diary-day-summary", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                $"{{\"items\":[{{\"localDate\":\"{date}\",\"diaryCount\":2,\"transactionCount\":3}}]}}"),
            "journal" => Json(
                HttpStatusCode.OK,
                "{\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"localDate\":\"2026-07-13\",\"title\":\"Review\",\"content\":\"Notes\",\"createdAt\":\"2026-07-13T00:00:00Z\",\"updatedAt\":\"2026-07-13T00:00:00Z\"}]}"),
            "performance" => Json(
                HttpStatusCode.OK,
                $"{{\"localDate\":\"{date}\",\"pnlAmount\":12.5,\"capitalBase\":1000,\"pnlPercent\":1.25,\"note\":\"steady\"}}"),
            "discipline" => Json(
                HttpStatusCode.OK,
                "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"content\":\"Follow the plan\",\"position\":0,\"createdAt\":\"2026-07-13T00:00:00Z\",\"updatedAt\":\"2026-07-13T00:00:00Z\"}"),
            "reminder" => Json(HttpStatusCode.OK, $"{{\"date\":\"{date}\",\"count\":4}}"),
            _ => Json(HttpStatusCode.OK, "{}")
        };
    }

    private static HttpResponseMessage CalendarResponse(string service, string path)
    {
        return service switch
        {
            "journal" => Json(
                HttpStatusCode.OK,
                "{\"items\":[{\"localDate\":\"2026-07-01\",\"diaryCount\":2,\"transactionCount\":3}]}"),
            "performance" when path.Contains("daily-performances", StringComparison.Ordinal) => Json(
                HttpStatusCode.OK,
                "{\"items\":[{\"localDate\":\"2026-07-01\",\"pnlAmount\":12.5,\"capitalBase\":1000,\"pnlPercent\":1.25,\"note\":\"steady\"}]}"),
            "performance" => Json(
                HttpStatusCode.OK,
                "{\"year\":2026,\"month\":7,\"total\":12.5,\"recordedDays\":1,\"profitDays\":1,\"lossDays\":0,\"flatDays\":0,\"bestDay\":12.5,\"worstDay\":12.5}"),
            "reminder" => Json(
                HttpStatusCode.OK,
                "{\"items\":[{\"localDate\":\"2026-07-01\",\"count\":7}]}"),
            _ => Json(HttpStatusCode.OK, "{}")
        };
    }

    private static string? QueryValue(string path, string name)
    {
        var query = path[(path.IndexOf('?') + 1)..];
        if (path.IndexOf('?') < 0) return null;
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == name)
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class DownstreamHandler(string service, Func<string, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(service, request.RequestUri!.PathAndQuery));
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        internal new const string Scheme = "Test";

        public TestAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim("sub", "33333333-3333-3333-3333-333333333333"),
                new Claim("timezone", "Asia/Taipei")
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
