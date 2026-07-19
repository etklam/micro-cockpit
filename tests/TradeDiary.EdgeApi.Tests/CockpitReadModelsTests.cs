using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public sealed class CockpitCompositionTests
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
    public async Task Bootstrap_returns_safe_typed_user_context()
    {
        using var factory = CreateFactory((service, path) => service == "identity"
            ? path.Contains("/settings", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, "{\"email\":\"owner@example.com\",\"displayName\":\"Owner\",\"timezone\":\"Asia/Taipei\",\"baseCurrency\":\"USD\",\"appearance\":\"dark\",\"locale\":\"zh-Hant\",\"updatedAt\":\"2026-07-18T00:00:00Z\"}")
                : Json(HttpStatusCode.OK, "{\"id\":\"33333333-3333-3333-3333-333333333333\",\"email\":\"owner@example.com\",\"displayName\":\"Owner\",\"timezone\":\"Asia/Taipei\",\"baseCurrency\":\"USD\",\"role\":\"user\",\"accountType\":\"human\",\"status\":\"active\",\"statusVersion\":1,\"appearance\":\"dark\",\"locale\":\"zh-Hant\"}")
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/bootstrap");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("owner@example.com", document.RootElement.GetProperty("currentUser").GetProperty("email").GetString());
        Assert.Equal("dark", document.RootElement.GetProperty("appearance").GetString());
        Assert.Equal("zh-Hant", document.RootElement.GetProperty("locale").GetString());
        Assert.False(document.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(document.RootElement.TryGetProperty("serviceUrl", out _));
    }

    [Fact]
    public async Task Settings_get_and_put_forward_to_identity()
    {
        var paths = new List<string>();
        using var factory = CreateFactory((service, path) =>
        {
            if (service == "identity") paths.Add(path.Split('?')[0]);
            if (service == "identity" && path.StartsWith("/internal/auth/settings", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"email\":\"owner@example.com\",\"displayName\":\"Owner\",\"timezone\":\"UTC\",\"baseCurrency\":\"USD\",\"appearance\":\"light\",\"locale\":\"en\",\"updatedAt\":\"2026-07-18T00:00:00Z\"}");
            return Json(HttpStatusCode.OK, "{}");
        });

        using var client = factory.CreateClient();
        using var get = await client.GetAsync("/api/app/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var put = await client.PutAsync("/api/app/settings", new StringContent(
            "{\"displayName\":\"Owner\",\"timezone\":\"UTC\",\"baseCurrency\":\"USD\",\"appearance\":\"light\",\"locale\":\"en\"}",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var body = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        Assert.Equal("light", body.RootElement.GetProperty("appearance").GetString());
        Assert.Equal("en", body.RootElement.GetProperty("locale").GetString());
        Assert.Contains("/internal/auth/settings", paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Optional_authorization_failure_fails_the_whole_dashboard(HttpStatusCode status)
    {
        using var factory = CreateFactory((service, path) => service == "reminder"
            ? Json(status, "{}")
            : DashboardResponse(service, path));
        using var response = await factory.CreateClient().GetAsync("/api/app/dashboard");

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task Missing_daily_performance_remains_null()
    {
        using var factory = CreateFactory((service, path) => service == "performance"
            ? Json(HttpStatusCode.NotFound, "{}")
            : DashboardResponse(service, path));
        using var response = await factory.CreateClient().GetAsync("/api/app/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("performance").ValueKind);
    }

    [Fact]
    public async Task Invalid_required_payload_returns_502_problem()
    {
        using var factory = CreateFactory((service, path) => service == "journal"
            ? Json(HttpStatusCode.OK, "not-json")
            : DashboardResponse(service, path));
        using var response = await factory.CreateClient().GetAsync("/api/app/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("downstream_invalid_response", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Stock_page_succeeds_without_market_data()
    {
        using var factory = CreateFactory((service, _) => service switch
        {
            "stock-research" => Json(HttpStatusCode.OK, "{\"id\":\"44444444-4444-4444-4444-444444444444\",\"symbol\":\"AAPL\",\"name\":\"Apple\",\"exchange\":\"NASDAQ\",\"assetType\":\"stock\",\"createdAt\":\"2026-07-01T00:00:00Z\"}"),
            "market-data" => Json(HttpStatusCode.ServiceUnavailable, "{}"),
            _ => Json(HttpStatusCode.OK, "{}")
        });
        using var response = await factory.CreateClient().GetAsync("/api/app/stocks/AAPL/page");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bars").ValueKind);
        Assert.Equal("unavailable", document.RootElement.GetProperty("capabilities").GetProperty("marketData").GetString());
    }

    [Fact]
    public async Task Required_downstream_timeout_returns_504()
    {
        using var factory = CreateAsyncFactory(async (service, path, cancellationToken) =>
        {
            if (service == "journal") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return DashboardResponse(service, path);
        });
        using var response = await factory.CreateClient().GetAsync("/api/app/dashboard");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("downstream_timeout", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rotation_monitor_rejects_invalid_downstream_payload()
    {
        using var factory = CreateFactory((service, _) => service == "rotation"
            ? Json(HttpStatusCode.OK, "not-json")
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("downstream_invalid_response", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rotation_monitor_rejects_unknown_rank_scope()
    {
        var malformed = RotationResponse.Replace(
            "\"rankScope\":\"sector\"",
            "\"rankScope\":\"portfolio\"");
        using var factory = CreateFactory((service, _) => service == "rotation"
            ? Json(HttpStatusCode.OK, malformed)
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("downstream_invalid_response", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rotation_monitor_rejects_missing_sector_rank_group()
    {
        var malformed = RotationResponse.Replace(
            ",\"rankGroup\":\"Technology\"",
            "");
        using var factory = CreateFactory((service, _) => service == "rotation"
            ? Json(HttpStatusCode.OK, malformed)
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("downstream_invalid_response", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rotation_monitor_rejects_mismatched_rank_group()
    {
        var malformed = RotationResponse.Replace(
            "\"rankGroup\":\"Technology\"",
            "\"rankGroup\":\"Global\"");
        using var factory = CreateFactory((service, _) => service == "rotation"
            ? Json(HttpStatusCode.OK, malformed)
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("downstream_invalid_response", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rotation_monitor_preserves_typed_null_fields()
    {
        using var factory = CreateFactory((service, _) => service == "rotation"
            ? Json(HttpStatusCode.OK, RotationResponse)
            : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sector", document.RootElement.GetProperty("universe").GetProperty("rankScope").GetString());
        Assert.Equal("risk_on", document.RootElement.GetProperty("marketState").GetProperty("state").GetString());
        Assert.Equal("Technology", document.RootElement.GetProperty("etfs")[0].GetProperty("rankGroup").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("etfs")[0].GetProperty("close").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("etfs")[0].GetProperty("aboveMa200").ValueKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rotation_monitor_preserves_downstream_authorization_failure(HttpStatusCode status)
    {
        using var factory = CreateFactory((service, _) => service == "rotation" ? Json(status, "{}") : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task Rotation_monitor_timeout_is_not_an_empty_success()
    {
        using var factory = CreateAsyncFactory(async (service, _, cancellationToken) =>
        {
            if (service == "rotation") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var response = await factory.CreateClient().GetAsync("/api/app/rotation/monitor?universe=SECTORS");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("downstream_timeout", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Request_cancellation_reaches_downstream_calls()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = CreateAsyncFactory(async (service, path, cancellationToken) =>
        {
            if (service == "journal")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { observed.TrySetResult(); throw; }
            }
            return DashboardResponse(service, path);
        });
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/api/app/dashboard", cancellation.Token));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
    public async Task Review_items_are_typed_and_forward_validated_filters_to_journal()
    {
        string? forwarded = null;
        using var factory = CreateFactory((service, path) =>
        {
            if (service == "journal")
            {
                forwarded = path;
                return Json(HttpStatusCode.OK, "{\"items\":[{\"diaryId\":\"11111111-1111-1111-1111-111111111111\",\"localDate\":\"2026-07-03\",\"title\":\"Evidence\",\"contentPreview\":\"Notes\",\"reviewStatus\":\"reviewed\",\"processAssessment\":\"good\",\"emotion\":\"calm\",\"disciplineScore\":4,\"executionScore\":3,\"mistakeTags\":[\"no_plan\"],\"lesson\":\"Lesson\",\"nextAction\":\"Next\",\"reviewUpdatedAt\":\"2026-07-03T00:00:00Z\"}],\"nextCursor\":null}");
            }
            return Json(HttpStatusCode.OK, "{}");
        });

        using var response = await factory.CreateClient().GetAsync("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31&status=reviewed&assessment=good&tag=no_plan&limit=25");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("reviewed", document.RootElement.GetProperty("items")[0].GetProperty("reviewStatus").GetString());
        Assert.Contains("status=reviewed", forwarded);
        Assert.Contains("assessment=good", forwarded);
        Assert.Contains("tag=no_plan", forwarded);
        Assert.Contains("limit=25", forwarded);
    }

    [Theory]
    [InlineData("/api/app/diary-review-items?from=2026-01-01&to=2026-03-04")]
    [InlineData("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31&limit=101")]
    [InlineData("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31&cursor=malformed")]
    [InlineData("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31&tag=unknown")]
    [InlineData("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31&status=pending")]
    public async Task Review_items_reject_invalid_query_values(string path)
    {
        using var factory = CreateFactory((_, _) => Json(HttpStatusCode.OK, "{\"items\":[],\"nextCursor\":null}"));
        using var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Review_items_preserve_downstream_failures(HttpStatusCode status)
    {
        using var factory = CreateFactory((service, _) => service == "journal" ? Json(status, "{}") : Json(HttpStatusCode.OK, "{}"));
        using var response = await factory.CreateClient().GetAsync("/api/app/diary-review-items?from=2026-07-01&to=2026-07-31");
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public void ResolveLocalDate_uses_timezone_claim_at_utc_boundary()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("timezone", "America/Los_Angeles")]));
        var utc = new DateTimeOffset(2026, 7, 14, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 7, 13), CockpitComposition.ResolveLocalDate(user, utc));
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

                foreach (var service in new[] { "identity", "journal", "performance", "discipline", "reminder", "stock-research", "market-data", "rotation" })
                {
                    services.AddHttpClient(service)
                        .ConfigurePrimaryHttpMessageHandler(() => new DownstreamHandler(service, responder));
                }
            }));
    }

    private static WebApplicationFactory<Program> CreateAsyncFactory(Func<string, string, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Edge:DownstreamTimeoutSeconds", "1");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                foreach (var service in new[] { "identity", "journal", "performance", "discipline", "reminder", "stock-research", "market-data", "rotation" })
                    services.AddHttpClient(service).ConfigurePrimaryHttpMessageHandler(() => new AsyncDownstreamHandler(service, responder));
            });
        });
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

    private const string RotationResponse = """
        {"universe":{"id":"55555555-5555-5555-5555-555555555555","code":"SECTORS","name":"US sectors","rankScope":"sector"},"snapshotDate":"2026-07-15","formulaVersion":"rotation-v1","status":"ok","marketState":{"state":"risk_on","breadthPercent":62.5,"benchmarkAboveMa200":true,"status":"ok"},"sectorBreadth":[{"sector":"Technology","memberCount":2,"availableCount":2,"aboveMa20Percent":100,"aboveMa50Percent":50,"aboveMa200Percent":50,"status":"ok"}],"etfs":[{"symbol":"XLK","label":"Technology","sector":"Technology","close":null,"return2w":4.5,"return1m":8.1,"return3m":12.2,"rank2w":1,"rankGroup":"Technology","percentile2w":1,"aboveMa20":true,"aboveMa50":true,"aboveMa200":null,"status":"ok"}]}
        """;

    private sealed class DownstreamHandler(string service, Func<string, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(service, request.RequestUri!.PathAndQuery));
    }

    private sealed class AsyncDownstreamHandler(string service, Func<string, string, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(service, request.RequestUri!.PathAndQuery, cancellationToken);
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
