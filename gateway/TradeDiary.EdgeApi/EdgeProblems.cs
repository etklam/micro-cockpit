using Microsoft.AspNetCore.Diagnostics;

internal static class EdgeProblems
{
    internal static IResult InvalidRequest(HttpContext context, string detail = "The request is invalid.") =>
        Create(context, StatusCodes.Status400BadRequest, "invalid_request", "Invalid request", detail);

    internal static IResult DownstreamInvalid(HttpContext context) =>
        Create(context, StatusCodes.Status502BadGateway, "downstream_invalid_response", "Invalid downstream response", "A required service returned an invalid response.");

    internal static IResult DownstreamUnavailable(HttpContext context) =>
        Create(context, StatusCodes.Status503ServiceUnavailable, "downstream_unavailable", "Service unavailable", "A required service is unavailable.");

    internal static IResult DownstreamTimeout(HttpContext context) =>
        Create(context, StatusCodes.Status504GatewayTimeout, "downstream_timeout", "Service timeout", "A required service did not respond in time.");

    internal static IResult FromStatus(HttpContext context, int status) => status switch
    {
        StatusCodes.Status400BadRequest => InvalidRequest(context),
        StatusCodes.Status401Unauthorized => Create(context, status, "authentication_required", "Authentication required", "Authentication is required."),
        StatusCodes.Status403Forbidden => Create(context, status, "access_denied", "Access denied", "You do not have access to this operation."),
        StatusCodes.Status404NotFound => Create(context, status, "resource_not_found", "Resource not found", "The requested resource was not found."),
        StatusCodes.Status409Conflict => Create(context, status, "conflict", "Conflict", "The request conflicts with the current resource state."),
        StatusCodes.Status422UnprocessableEntity => Create(context, status, "validation_failed", "Validation failed", "The request failed validation."),
        StatusCodes.Status502BadGateway => DownstreamInvalid(context),
        StatusCodes.Status504GatewayTimeout => DownstreamTimeout(context),
        _ => DownstreamUnavailable(context)
    };

    internal static Task WriteStatusCodeAsync(StatusCodeContext statusContext)
    {
        var context = statusContext.HttpContext;
        if (context.Response.HasStarted || context.Response.ContentLength is > 0 || !IsSupported(context.Response.StatusCode))
            return Task.CompletedTask;
        return FromStatus(context, context.Response.StatusCode).ExecuteAsync(context);
    }

    private static bool IsSupported(int status) => status is
        StatusCodes.Status400BadRequest or
        StatusCodes.Status401Unauthorized or
        StatusCodes.Status403Forbidden or
        StatusCodes.Status404NotFound or
        StatusCodes.Status409Conflict or
        StatusCodes.Status422UnprocessableEntity or
        StatusCodes.Status502BadGateway or
        StatusCodes.Status503ServiceUnavailable or
        StatusCodes.Status504GatewayTimeout;

    private static IResult Create(HttpContext context, int status, string code, string title, string detail) =>
        Results.Json(
            new EdgeProblemDetails(code, title, status, detail, CorrelationMiddleware.Get(context)),
            statusCode: status,
            contentType: "application/problem+json");
}

internal sealed class EdgeExceptionMiddleware(RequestDelegate next, ILogger<EdgeExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (BadHttpRequestException)
        {
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            await EdgeProblems.InvalidRequest(context).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled Edge request failure. CorrelationId: {CorrelationId}", CorrelationMiddleware.Get(context));
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            await EdgeProblems.DownstreamUnavailable(context).ExecuteAsync(context);
        }
    }
}

internal sealed class CorrelationMiddleware(RequestDelegate next)
{
    internal const string ItemKey = "correlationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        var correlationId = string.IsNullOrWhiteSpace(supplied) ? Guid.NewGuid().ToString() : supplied;
        context.Items[ItemKey] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        await next(context);
    }

    internal static string Get(HttpContext context) =>
        context.Items[ItemKey]?.ToString() ?? context.TraceIdentifier;
}
