using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public sealed class AuthRateLimitingTests
{
    private static readonly string[] Services =
    [
        "identity", "journal", "performance", "discipline", "reminder", "stock-research",
        "market-data", "price-alert", "rotation", "partner", "content", "tool", "operations"
    ];

    [Fact]
    public async Task Registration_allows_requests_below_threshold()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody($"user{i}@example.test"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task Excess_registration_requests_return_429_with_retry_after()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", RegisterBody($"ok{i}@example.test"))).StatusCode);

        using var limited = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("limited@example.test"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.True(int.TryParse(limited.Headers.RetryAfter?.ToString() ?? limited.Headers.GetValues("Retry-After").First(), out var retryAfter));
        Assert.True(retryAfter > 0);

        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limited", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Login_and_refresh_use_separate_policies()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.co", password = "correct horse battery staple" })).StatusCode);

        using var loginLimited = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.co", password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.TooManyRequests, loginLimited.StatusCode);

        // Refresh uses a different partition policy; still available after login is exhausted.
        using var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Registration_key_reaches_identity_register_only()
    {
        var observed = new List<(string Path, bool HasRegistrationKey)>();
        using var factory = CreateFactory(observed);
        using var client = factory.CreateClient();

        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(RegisterBody("key@example.test"))
        };
        register.Headers.Add("X-Registration-Key", "secret-registration");
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(register)).StatusCode);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "key@example.test", password = "correct horse battery staple" })
        };
        login.Headers.Add("X-Registration-Key", "secret-registration");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);

        using var apiKey = new HttpRequestMessage(HttpMethod.Post, "/api/auth/api-key/token")
        {
            Content = JsonContent.Create(new { apiKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-real-key")) })
        };
        apiKey.Headers.Add("X-Registration-Key", "secret-registration");
        await client.SendAsync(apiKey);

        Assert.Contains(observed, r => r.Path.Contains("/internal/auth/register", StringComparison.Ordinal) && r.HasRegistrationKey);
        Assert.DoesNotContain(observed, r => r.Path.Contains("/internal/auth/login", StringComparison.Ordinal) && r.HasRegistrationKey);
        Assert.DoesNotContain(observed, r => r.Path.Contains("/internal/auth/api-key/token", StringComparison.Ordinal) && r.HasRegistrationKey);
    }

    [Fact]
    public async Task Arbitrary_forwarded_for_is_not_trusted_for_rate_limit_partition()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Exhaust the real client-IP partition first.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", RegisterBody($"base{i}@example.test"))).StatusCode);

        // Spoofed X-Forwarded-For must not open a fresh partition when no trusted proxy is configured.
        using var spoofed = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(RegisterBody("spoofed-out@example.test"))
        };
        spoofed.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.9");
        using var response = await client.SendAsync(spoofed);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/api-key/token")]
    public async Task Oversized_auth_bodies_return_413_and_do_not_reach_identity(string path)
    {
        var observed = new List<(string Path, bool HasRegistrationKey)>();
        using var factory = CreateFactory(observed);
        using var client = factory.CreateClient();

        var oversized = new string('x', 16_385);
        using var content = new StringContent(
            path.Contains("api-key", StringComparison.Ordinal)
                ? $$"""{"apiKey":"{{oversized}}"}"""
                : path.Contains("login", StringComparison.Ordinal)
                    ? $$"""{"email":"a@b.co","password":"{{oversized}}"}"""
                    : $$"""{"email":"a@b.co","password":"{{oversized}}","displayName":"T","timezone":"UTC","baseCurrency":"USD"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(path, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("payload_too_large", problem.GetProperty("code").GetString());
        Assert.DoesNotContain(observed, r => r.Path.Contains("/internal/auth/", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory(List<(string Path, bool HasRegistrationKey)>? observed = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var service in Services)
            {
                services.AddHttpClient(service).ConfigurePrimaryHttpMessageHandler(() =>
                    observed is null ? new IdentityOkHandler() : new CapturingHandler(observed));
            }
        }));

    private static object RegisterBody(string email) => new
    {
        email,
        password = "correct horse battery staple",
        displayName = "Test User",
        timezone = "UTC",
        baseCurrency = "USD"
    };

    private class IdentityOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("/internal/auth/register", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"id":"11111111-1111-1111-1111-111111111111","email":"a@b.co","displayName":"T","timezone":"UTC","baseCurrency":"USD"}""", Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsolutePath.Contains("/internal/auth/login", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"accessToken":"access","expiresAt":"2026-07-18T00:00:00Z","refreshToken":"refresh"}""", Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsolutePath.Contains("/internal/auth/refresh", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            if (request.RequestUri?.AbsolutePath.Contains("/internal/auth/api-key/token", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CapturingHandler(List<(string Path, bool HasRegistrationKey)> requests) : IdentityOkHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add((request.RequestUri?.AbsolutePath ?? string.Empty, request.Headers.Contains("X-Registration-Key")));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
