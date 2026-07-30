using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class CockpitCompositionTests
{
    // The identical Journal Day behavior matrix mirrored from journal-service (QuickObservationApiTests).
    // Edge re-implements JournalDay.Resolve (ADR-0001 forbids a shared kernel), so these assert
    // cross-adapter parity: the date Bootstrap/Calendar derive must equal the date a Journal write uses.
    [Theory]
    [InlineData("2026-07-14T01:29:59Z", "America/Los_Angeles", "18:30", "2026-07-12")]
    [InlineData("2026-07-14T01:30:00Z", "America/Los_Angeles", "18:30", "2026-07-13")]
    [InlineData("2026-03-08T09:30:00Z", "America/Los_Angeles", "02:00", "2026-03-07")]
    [InlineData("2026-03-08T10:00:00Z", "America/Los_Angeles", "02:00", "2026-03-08")]
    [InlineData("2026-11-01T08:29:59Z", "America/Los_Angeles", "01:30", "2026-10-31")]
    [InlineData("2026-11-01T08:30:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    [InlineData("2026-11-01T09:00:00Z", "America/Los_Angeles", "01:30", "2026-11-01")]
    public void ResolveJournalDay_matches_journal_service_matrix(string instant, string timezone, string rollover, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), CockpitComposition.ResolveJournalDay(timezone, rollover, DateTimeOffset.Parse(instant)));
    }

    [Theory]
    [InlineData("not-a-zone", "00:00", "invalid_timezone")]
    [InlineData("America/Los_Angeles", "25:00", "invalid_rollover")]
    public void ResolveJournalDay_treats_invalid_preferences_as_controlled_failure(string timezone, string rollover, string message)
    {
        var ex = Assert.Throws<ArgumentException>(() => CockpitComposition.ResolveJournalDay(timezone, rollover, DateTimeOffset.Parse("2026-07-14T01:30:00Z")));
        Assert.StartsWith(message, ex.Message);
    }

    [Fact]
    public async Task Bootstrap_derives_journal_day_from_rollover_not_clock_time()
    {
        // 2026-07-13 17:30 UTC is still the 13th by the wall clock in Asia/Taipei (UTC+8 → 02:30 on the 14th),
        // but with a 06:00 rollover the Journal Day is still the 13th — i.e. the non-midnight rollover must win.
        using var factory = CreateFactory((service, path) =>
        {
            if (service != "identity") return Json(HttpStatusCode.OK, "{}");
            return path.Contains("/settings", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK,
                    """{"email":"owner@example.com","displayName":"Owner","timezone":"Asia/Taipei","journalDayRollover":"06:00","baseCurrency":"USD","appearance":"dark","locale":"zh-Hant","accentTheme":"red","updatedAt":"2026-07-18T00:00:00Z"}""")
                : Json(HttpStatusCode.OK,
                    """{"id":"33333333-3333-3333-3333-333333333333","email":"owner@example.com","displayName":"Owner","timezone":"Asia/Taipei","journalDayRollover":"06:00","baseCurrency":"USD","role":"user","accountType":"human","status":"active","statusVersion":1,"appearance":"dark","locale":"zh-Hant","accentTheme":"red"}""");
        }, new DateTimeOffset(2026, 7, 13, 17, 30, 0, TimeSpan.Zero));

        using var response = await factory.CreateClient().GetAsync("/api/app/bootstrap");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-07-13", document.RootElement.GetProperty("currentJournalDay").GetString());
    }

    [Fact]
    public async Task Bootstrap_rejects_invalid_persisted_timezone_with_controlled_error()
    {
        using var factory = CreateFactory((service, path) =>
        {
            if (service != "identity") return Json(HttpStatusCode.OK, "{}");
            return path.Contains("/settings", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK,
                    """{"email":"owner@example.com","displayName":"Owner","timezone":"Bogus/Zone","journalDayRollover":"00:00","baseCurrency":"USD","appearance":"dark","locale":"zh-Hant","accentTheme":"red","updatedAt":"2026-07-18T00:00:00Z"}""")
                : Json(HttpStatusCode.OK,
                    """{"id":"33333333-3333-3333-3333-333333333333","email":"owner@example.com","displayName":"Owner","timezone":"Bogus/Zone","journalDayRollover":"00:00","baseCurrency":"USD","role":"user","accountType":"human","status":"active","statusVersion":1,"appearance":"dark","locale":"zh-Hant","accentTheme":"red"}""");
        });

        using var response = await factory.CreateClient().GetAsync("/api/app/bootstrap");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

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
        Func<string, string, HttpResponseMessage> responder,
        DateTimeOffset? nowUtc = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                if (nowUtc is not null)
                    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(nowUtc.Value)));
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
