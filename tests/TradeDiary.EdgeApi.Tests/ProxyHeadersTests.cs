using Microsoft.AspNetCore.Http;

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
}
