using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

internal static class AuthRateLimiting
{
    internal const string Register = "auth-register";
    internal const string Login = "auth-login";
    internal const string Refresh = "auth-refresh";
    private const long AuthBodyBytes = 16_384;

    // Partition by RemoteIpAddress after UseForwardedHeaders. Without configured trusted
    // proxies, X-Forwarded-For is ignored and the direct peer IP is used.
    internal static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = WriteRejectedAsync;

        options.AddPolicy(Register, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        options.AddPolicy(Login, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        options.AddPolicy(Refresh, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    internal static RouteHandlerBuilder LimitAuthBody(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var feature = context.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
                feature.MaxRequestBodySize = AuthBodyBytes;
            return await next(context);
        });

    private static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask WriteRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await EdgeProblems.TooManyRequests(httpContext).ExecuteAsync(httpContext);
    }
}
