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

    public EdgeEndpointTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_health_endpoint_is_public()
    {
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class EdgeAuthorizationTests
{
    private static readonly string[] Services = ["identity", "journal", "performance", "discipline", "reminder", "stock-research", "market-data", "price-alert", "rotation", "partner", "content", "tool", "operations"];

    [Theory]
    [InlineData("GET", "/api/app/stocks", "research:read", HttpStatusCode.OK)]
    [InlineData("POST", "/api/app/stocks", "research:read", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "/api/app/stocks/11111111-1111-1111-1111-111111111111/note", "research:read", HttpStatusCode.Forbidden)]
    [InlineData("POST", "/api/app/stocks/11111111-1111-1111-1111-111111111111/timeline", "research:read", HttpStatusCode.Forbidden)]
    [InlineData("GET", "/api/app/diaries", "", HttpStatusCode.Forbidden)]
    [InlineData("POST", "/api/app/diaries", "diary:read", HttpStatusCode.Forbidden)]
    [InlineData("POST", "/api/app/diaries", "diary:write", HttpStatusCode.OK)]
    [InlineData("POST", "/api/app/tools/profit-loss", "research:read", HttpStatusCode.Forbidden)]
    public async Task Agent_scope_matrix_is_enforced(string method, string path, string scopes, HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Account-Type", "agent");
        request.Headers.Add("X-Test-Scopes", scopes);
        if (method is "POST" or "PUT") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_cannot_read_audit_but_admin_can()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/admin/operations/audit");
        denied.Headers.Add("X-Test-Account-Type", "human");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(denied)).StatusCode);
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/admin/operations/audit");
        allowed.Headers.Add("X-Test-Account-Type", "human"); allowed.Headers.Add("X-Test-Role", "admin");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(allowed)).StatusCode);
    }

    [Theory]
    [InlineData("human", HttpStatusCode.MethodNotAllowed)]
    [InlineData("agent", HttpStatusCode.Forbidden)]
    public async Task Public_edge_has_no_audit_write_route(string accountType, HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/operations/audit") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Test-Account-Type", accountType);
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.AddAuthentication(options => { options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme; options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme; })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { });
        foreach (var service in Services) services.AddHttpClient(service).ConfigurePrimaryHttpMessageHandler(() => new OkHandler());
    }));

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "test";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accountType = Request.Headers["X-Test-Account-Type"].FirstOrDefault();
        if (accountType is null) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()), new("account_type", accountType) };
        claims.AddRange((Request.Headers["X-Test-Scopes"].FirstOrDefault() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(scope => new Claim("scope", scope)));
        var role = Request.Headers["X-Test-Role"].FirstOrDefault(); if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme, "name", ClaimTypes.Role));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationScheme)));
    }
}
