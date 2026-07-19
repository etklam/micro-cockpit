using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;

internal static class AuthRateLimiting
{
    internal const string Register = "auth-register";
    internal const string Login = "auth-login";
    internal const string Refresh = "auth-refresh";
    internal const long AuthBodyBytes = 16_384;

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

        // Partner invitation create/redeem — IP partition (same pattern as auth; rate limiter runs pre-auth).
        options.AddPolicy(PartnerRateLimiting.InviteCreate, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        options.AddPolicy(PartnerRateLimiting.InviteRedeem, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        options.AddPolicy(PartnerRateLimiting.InviteRead, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    // Endpoint metadata is applied by AuthBodyLimitMiddleware before model binding.
    // Do not use an endpoint filter alone: filters run after Minimal API body binding.
    internal static RouteHandlerBuilder LimitAuthBody(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new AuthBodySizeLimit(AuthBodyBytes));

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

internal sealed class AuthBodySizeLimit(long maxRequestBodySize) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = maxRequestBodySize;
}

/// <summary>
/// Applies auth body size limits after endpoint selection and before body binding.
/// Rejects oversized Content-Length immediately so Identity/password hashing is never reached.
/// </summary>
internal sealed class AuthBodyLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
        if (limit is long max)
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
                feature.MaxRequestBodySize = max;

            if (context.Request.ContentLength is long length && length > max)
            {
                await EdgeProblems.PayloadTooLarge(context).ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
