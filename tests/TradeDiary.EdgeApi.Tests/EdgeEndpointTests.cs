using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class EdgeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public EdgeEndpointTests(WebApplicationFactory<Program> factory) => client = factory.CreateClient();

    [Fact]
    public async Task Live_health_endpoint_is_public()
    {
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class EdgeAuthorizationTests
{
    private static readonly string[] Services = ["identity", "journal", "market-data", "tool"];

    [Theory]
    [InlineData("GET", "/api/app/market-observations/today", "journal:read", HttpStatusCode.OK)]
    [InlineData("GET", "/api/app/market-observations?tag=breadth&limit=10", "journal:read", HttpStatusCode.OK)]
    [InlineData("POST", "/api/app/quick-observations", "journal:read", HttpStatusCode.Forbidden)]
    [InlineData("POST", "/api/app/quick-observations", "journal:write", HttpStatusCode.OK)]
    [InlineData("PUT", "/api/app/observation-updates/11111111-1111-1111-1111-111111111111", "journal:write", HttpStatusCode.OK)]
    [InlineData("GET", "/api/agent/journal-records?limit=10", "", HttpStatusCode.Forbidden)]
    [InlineData("GET", "/api/agent/journal-records?limit=10", "agent:read", HttpStatusCode.OK)]
    [InlineData("GET", "/api/agent/journal-changes?cursor=test", "", HttpStatusCode.Forbidden)]
    [InlineData("GET", "/api/agent/journal-changes?cursor=test", "agent:read", HttpStatusCode.OK)]
    [InlineData("POST", "/api/app/tools/profit-loss", "agent:read", HttpStatusCode.Forbidden)]
    public async Task Agent_scope_matrix_is_enforced(
        string method,
        string path,
        string scopes,
        HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Account-Type", "agent");
        request.Headers.Add("X-Test-Scopes", scopes);
        if (method is "POST" or "PUT")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/app/dashboard")]
    [InlineData("/api/app/diaries")]
    [InlineData("/api/app/diary-alerts")]
    [InlineData("/api/app/diary-review-summary")]
    [InlineData("/api/app/price-alerts")]
    [InlineData("/api/app/rotation/monitor")]
    [InlineData("/api/app/partners")]
    [InlineData("/api/content/posts")]
    [InlineData("/api/app/stocks")]
    [InlineData("/api/admin/operations/audit")]
    public async Task Retired_routes_are_not_mapped(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Account-Type", "human");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme,
                        _ => { });
                foreach (var service in Services)
                    services.AddHttpClient(service)
                        .ConfigurePrimaryHttpMessageHandler(() => new OkHandler());
            }));

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accountType = Request.Headers["X-Test-Account-Type"].FirstOrDefault();
        if (accountType is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("account_type", accountType),
        };
        claims.AddRange((Request.Headers["X-Test-Scopes"].FirstOrDefault() ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(scope => new Claim("scope", scope)));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, AuthenticationScheme, "name", ClaimTypes.Role));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, AuthenticationScheme)));
    }
}
