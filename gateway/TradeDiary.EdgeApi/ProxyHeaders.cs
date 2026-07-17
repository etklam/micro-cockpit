using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

public static class ProxyHeaders
{
    private static readonly string[] ProxyKeys = ["ForwardedHeaders:KnownProxies", "Proxy:TrustedProxies"];
    private static readonly string[] NetworkKeys = ["ForwardedHeaders:KnownNetworks", "Proxy:TrustedNetworks"];

    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardLimit = 1;

        // Framework defaults include loopback. Empty KnownProxies/Networks does NOT mean
        // "trust nobody": the middleware then skips the known-proxy check and accepts any
        // client-supplied X-Forwarded-* headers. Only enable forwarding after explicit trust.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var value in ReadValues(configuration, ProxyKeys))
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException($"Invalid forwarded-header trusted proxy address: '{value}'.");

            options.KnownProxies.Add(address);
        }

        foreach (var value in ReadValues(configuration, NetworkKeys))
        {
            var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
                !int.TryParse(parts[1], out var prefixLength))
                throw new InvalidOperationException($"Invalid forwarded-header trusted network: '{value}'.");

            var maxPrefixLength = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
                throw new InvalidOperationException($"Invalid forwarded-header trusted network prefix: '{value}'.");

            options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefixLength));
        }

        options.ForwardedHeaders = options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
            : ForwardedHeaders.None;
    }

    public static bool ShouldUseSecureRefreshCookie(HttpContext context, IHostEnvironment environment) =>
        environment.IsProduction() || context.Request.IsHttps;

    // X-Registration-Key is never forwarded by default. Callers must opt in only for
    // POST /api/auth/register → Identity /internal/auth/register.
    public static void Forward(HttpRequestMessage request, HttpContext context, bool forwardRegistrationKey = false)
    {
        request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", context.Items["correlationId"]?.ToString());
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key) && !string.IsNullOrWhiteSpace(key.ToString()))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key.ToString());
        if (forwardRegistrationKey &&
            context.Request.Headers.TryGetValue("X-Registration-Key", out var registrationKey) &&
            !string.IsNullOrWhiteSpace(registrationKey.ToString()))
            request.Headers.TryAddWithoutValidation("X-Registration-Key", registrationKey.ToString());
    }

    public static void Propagate(HttpContext context, HttpResponseMessage response)
    {
        if (response.Headers.Location is not null) context.Response.Headers.Location = response.Headers.Location.ToString();
    }

    private static IEnumerable<string> ReadValues(IConfiguration configuration, IEnumerable<string> keys)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            foreach (var value in Split(configuration[key]))
                values.Add(value);

            foreach (var child in configuration.GetSection(key).GetChildren())
            {
                foreach (var value in Split(child.Value))
                    values.Add(value);
            }
        }

        return values;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
