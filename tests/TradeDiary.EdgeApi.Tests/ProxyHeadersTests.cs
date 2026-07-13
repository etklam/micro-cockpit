using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class ProxyHeadersTests
{
    [Fact]
    public void Forwards_auth_correlation_and_idempotency_headers()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer access";
        context.Request.Headers["Idempotency-Key"] = "retry-1";
        context.Items["correlationId"] = "corr-1";
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://service/internal/diaries");

        ProxyHeaders.Forward(request, context);

        Assert.Equal("Bearer access", request.Headers.Authorization?.ToString());
        Assert.Equal("corr-1", request.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("retry-1", request.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public void Propagates_location_metadata()
    {
        var context = new DefaultHttpContext();
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Created) { RequestMessage = new HttpRequestMessage() };
        response.Headers.Location = new Uri("http://service/internal/diaries/1");

        ProxyHeaders.Propagate(context, response);

        Assert.Equal("http://service/internal/diaries/1", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public void Does_not_trust_forwarded_headers_without_explicit_proxy_configuration()
    {
        var options = new ForwardedHeadersOptions();

        ProxyHeaders.ConfigureForwardedHeaders(options, new ConfigurationBuilder().Build());

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            options.ForwardedHeaders);
    }

    [Fact]
    public void Accepts_only_configured_proxy_and_network_sources()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies"] = "10.0.0.10",
            ["ForwardedHeaders:KnownNetworks"] = "192.0.2.0/24"
        }).Build();
        var options = new ForwardedHeadersOptions();

        ProxyHeaders.ConfigureForwardedHeaders(options, configuration);

        Assert.Equal([System.Net.IPAddress.Parse("10.0.0.10")], options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
    }

    [Fact]
    public void Production_refresh_cookie_is_secure_even_before_https_termination()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.True(ProxyHeaders.ShouldUseSecureRefreshCookie(context, HostEnvironment(Environments.Production)));
        Assert.False(ProxyHeaders.ShouldUseSecureRefreshCookie(context, HostEnvironment(Environments.Development)));

        context.Request.Scheme = "https";
        Assert.True(ProxyHeaders.ShouldUseSecureRefreshCookie(context, HostEnvironment(Environments.Development)));
    }

    private static IHostEnvironment HostEnvironment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = ".";
        public string WebRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
