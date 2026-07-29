using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public sealed class CockpitCompositionTests
{
    [Fact]
    public async Task Bootstrap_returns_only_cutover_product_areas()
    {
        using var factory = CreateFactory((service, path) =>
        {
            if (service != "identity") return Json(HttpStatusCode.OK, "{}");
            return path.Contains("/settings", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK,
                    """{"email":"owner@example.com","displayName":"Owner","timezone":"Asia/Taipei","journalDayRollover":"06:00","baseCurrency":"USD","appearance":"dark","locale":"zh-Hant","accentTheme":"red","updatedAt":"2026-07-18T00:00:00Z"}""")
                : Json(HttpStatusCode.OK,
                    """{"id":"33333333-3333-3333-3333-333333333333","email":"owner@example.com","displayName":"Owner","timezone":"Asia/Taipei","journalDayRollover":"06:00","baseCurrency":"USD","role":"user","accountType":"human","status":"active","statusVersion":1,"appearance":"dark","locale":"zh-Hant","accentTheme":"red"}""");
        });

        using var response = await factory.CreateClient().GetAsync("/api/app/bootstrap");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["today", "review", "watchlist", "calendar", "tools", "settings"],
            document.RootElement.GetProperty("availableProductAreas")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.False(document.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(document.RootElement.TryGetProperty("serviceUrl", out _));
    }

    [Fact]
    public async Task Calendar_composes_market_observation_days_from_journal_only()
    {
        var calls = new List<string>();
        using var factory = CreateFactory((service, path) =>
        {
            calls.Add(service);
            return service == "journal"
                ? Json(HttpStatusCode.OK,
                    """{"items":[{"date":"2026-07-01","marketObservationId":"11111111-1111-1111-1111-111111111111","updateCount":2,"readyForReviewCount":1}]}""")
                : Json(HttpStatusCode.OK, "{}");
        });

        using var response = await factory.CreateClient().GetAsync("/api/app/calendar?year=2026&month=7");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var days = document.RootElement.GetProperty("days").EnumerateArray().ToArray();
        var day = days[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-07-01", day.GetProperty("date").GetString());
        Assert.Equal(2, day.GetProperty("updateCount").GetInt32());
        Assert.Equal(1, day.GetProperty("readyForReviewCount").GetInt32());
        Assert.Equal(31, days.Length);
        Assert.Equal(["journal"], calls.Distinct().ToArray());
    }

    [Fact]
    public async Task Calendar_preserves_required_downstream_failure()
    {
        using var factory = CreateFactory((_, _) =>
            Json(HttpStatusCode.ServiceUnavailable, """{"error":"journal_unavailable"}"""));

        using var response = await factory.CreateClient().GetAsync("/api/app/calendar?year=2026&month=7");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<string, string, HttpResponseMessage> responder) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.Scheme,
                        _ => { });
                foreach (var service in new[] { "identity", "journal", "market-data", "tool" })
                    services.AddHttpClient(service)
                        .ConfigurePrimaryHttpMessageHandler(() =>
                            new DownstreamHandler(service, responder));
            }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class DownstreamHandler(
        string service,
        Func<string, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(service, request.RequestUri!.PathAndQuery));
    }

    private sealed class TestAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
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
                new Claim("timezone", "Asia/Taipei"),
                new Claim("account_type", "human"),
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
