using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;

internal enum DownstreamFailure
{
    None,
    Timeout,
    Unavailable,
    InvalidResponse
}

internal sealed record DownstreamResponse<T>(
    int StatusCode,
    T? Value,
    DownstreamFailure Failure = DownstreamFailure.None)
{
    internal bool IsSuccess => StatusCode is >= 200 and < 300 && Failure == DownstreamFailure.None;
}

internal sealed class EdgeTransport(IHttpClientFactory clients, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static EdgeTransport() => Json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

    private TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Edge:DownstreamTimeoutSeconds", 8), 1, 30));

    internal async Task<DownstreamResponse<T>> GetAsync<T>(string service, string path, HttpContext context)
    {
        var raw = await SendAsync(service, path, HttpMethod.Get, null, context);
        return Deserialize<T>(raw);
    }

    internal async Task<DownstreamResponse<TResponse>> SendJsonAsync<TRequest, TResponse>(
        string service,
        string path,
        HttpMethod method,
        TRequest body,
        HttpContext context,
        bool forwardRegistrationKey = false)
    {
        var raw = await SendAsync(service, path, method, JsonSerializer.Serialize(body, Json), context, forwardRegistrationKey);
        return Deserialize<TResponse>(raw);
    }

    private static DownstreamResponse<T> Deserialize<T>(RawDownstreamResponse raw)
    {
        if (raw.Failure != DownstreamFailure.None)
            return new DownstreamResponse<T>(raw.StatusCode, default, raw.Failure);
        if (raw.StatusCode is < 200 or >= 300)
            return new DownstreamResponse<T>(raw.StatusCode, default);
        if (string.IsNullOrWhiteSpace(raw.Body))
            return new DownstreamResponse<T>(StatusCodes.Status502BadGateway, default, DownstreamFailure.InvalidResponse);
        try
        {
            var value = JsonSerializer.Deserialize<T>(raw.Body, Json);
            return value is null
                ? new DownstreamResponse<T>(StatusCodes.Status502BadGateway, default, DownstreamFailure.InvalidResponse)
                : new DownstreamResponse<T>(raw.StatusCode, value);
        }
        catch (JsonException)
        {
            return new DownstreamResponse<T>(StatusCodes.Status502BadGateway, default, DownstreamFailure.InvalidResponse);
        }
        catch (NotSupportedException)
        {
            return new DownstreamResponse<T>(StatusCodes.Status502BadGateway, default, DownstreamFailure.InvalidResponse);
        }
    }

    internal async Task<IResult> ProxyAsync(string service, string path, HttpContext context, bool forwardRegistrationKey = false)
    {
        string? body = null;
        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }

        var result = await SendAsync(service, path, new HttpMethod(context.Request.Method), body, context, forwardRegistrationKey);
        if (result.Failure == DownstreamFailure.Timeout) return EdgeProblems.DownstreamTimeout(context);
        if (result.Failure == DownstreamFailure.Unavailable) return EdgeProblems.DownstreamUnavailable(context);
        if (result.StatusCode is < 200 or >= 300) return EdgeProblems.FromStatus(context, result.StatusCode);
        if (result.StatusCode == StatusCodes.Status204NoContent) return Results.NoContent();
        return Results.Content(result.Body ?? string.Empty, result.ContentType ?? "application/json", statusCode: result.StatusCode);
    }

    internal static RouteHandlerBuilder MapProxy(
        WebApplication app,
        string route,
        string service,
        string target,
        string[] methods,
        bool forwardRegistrationKey = false) =>
        app.MapMethods(route, methods, async (HttpContext context, EdgeTransport transport) =>
            await transport.ProxyAsync(service, Expand(target, context.Request.RouteValues) + context.Request.QueryString, context, forwardRegistrationKey));

    internal IResult ProblemFor<T>(DownstreamResponse<T> response, HttpContext context)
    {
        if (response.Failure == DownstreamFailure.Timeout) return EdgeProblems.DownstreamTimeout(context);
        if (response.Failure == DownstreamFailure.InvalidResponse) return EdgeProblems.DownstreamInvalid(context);
        if (response.Failure == DownstreamFailure.Unavailable) return EdgeProblems.DownstreamUnavailable(context);
        return EdgeProblems.FromStatus(context, response.StatusCode);
    }

    private async Task<RawDownstreamResponse> SendAsync(
        string service,
        string path,
        HttpMethod method,
        string? body,
        HttpContext context,
        bool forwardRegistrationKey = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }
        ProxyHeaders.Forward(request, context, forwardRegistrationKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await clients.CreateClient(service).SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            ProxyHeaders.Propagate(context, response);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return new RawDownstreamResponse((int)response.StatusCode, content, response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new RawDownstreamResponse(StatusCodes.Status504GatewayTimeout, null, null, DownstreamFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return new RawDownstreamResponse(StatusCodes.Status503ServiceUnavailable, null, null, DownstreamFailure.Unavailable);
        }
    }

    private static string Expand(string path, RouteValueDictionary values)
    {
        foreach (var (key, value) in values)
            path = path.Replace($"{{{key}}}", Uri.EscapeDataString(value?.ToString() ?? string.Empty), StringComparison.Ordinal);
        return path;
    }

    private sealed record RawDownstreamResponse(
        int StatusCode,
        string? Body,
        string? ContentType,
        DownstreamFailure Failure = DownstreamFailure.None);
}
