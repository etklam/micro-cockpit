public static class ProxyHeaders
{
    public static void Forward(HttpRequestMessage request, HttpContext context)
    {
        request.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", context.Items["correlationId"]?.ToString());
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key) && !string.IsNullOrWhiteSpace(key.ToString()))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key.ToString());
    }

    public static void Propagate(HttpContext context, HttpResponseMessage response)
    {
        if (response.Headers.Location is not null) context.Response.Headers.Location = response.Headers.Location.ToString();
    }
}
