using System.Text.Json;

internal static class AuthenticationEndpoints
{
    internal static void Map(WebApplication app)
    {
        EdgeTransport.MapProxy(app, "/api/auth/register", "identity", "/internal/auth/register", [HttpMethods.Post])
            .AllowAnonymous();
        app.MapPost("/api/auth/login", LoginAsync).AllowAnonymous();
        app.MapPost("/api/auth/refresh", RefreshAsync).AllowAnonymous();
        app.MapPost("/api/auth/logout", LogoutAsync).AllowAnonymous();
        EdgeTransport.MapProxy(app, "/api/auth/api-key/token", "identity", "/internal/auth/api-key/token", [HttpMethods.Post])
            .AllowAnonymous();
        EdgeTransport.MapProxy(app, "/api/app/agents", "identity", "/internal/auth/agents", [HttpMethods.Post]);
        EdgeTransport.MapProxy(app, "/api/app/api-keys/{id:guid}", "identity", "/internal/auth/api-keys/{id}", [HttpMethods.Delete]);
    }

    private static async Task<IResult> LoginAsync(JsonElement body, HttpContext context, EdgeTransport transport)
    {
        var response = await transport.SendJsonAsync<JsonElement, IdentityTokensResponse>(
            "identity", "/internal/auth/login", HttpMethod.Post, body, context);
        if (!response.IsSuccess) return transport.ProblemFor(response, context);
        SetRefreshCookie(context, response.Value!.RefreshToken);
        return Results.Ok(new SessionTokensResponse(response.Value.AccessToken, response.Value.ExpiresAt));
    }

    private static async Task<IResult> RefreshAsync(HttpContext context, EdgeTransport transport)
    {
        var cookie = context.Request.Cookies["td_refresh"];
        if (string.IsNullOrEmpty(cookie)) return EdgeProblems.FromStatus(context, StatusCodes.Status401Unauthorized);
        var response = await transport.SendJsonAsync<RefreshRequest, IdentityTokensResponse>(
            "identity", "/internal/auth/refresh", HttpMethod.Post, new RefreshRequest(cookie), context);
        if (!response.IsSuccess)
        {
            ClearRefreshCookie(context);
            return response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                ? EdgeProblems.FromStatus(context, StatusCodes.Status401Unauthorized)
                : transport.ProblemFor(response, context);
        }
        SetRefreshCookie(context, response.Value!.RefreshToken);
        return Results.Ok(new SessionTokensResponse(response.Value.AccessToken, response.Value.ExpiresAt));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, EdgeTransport transport)
    {
        var cookie = context.Request.Cookies["td_refresh"];
        if (!string.IsNullOrEmpty(cookie))
        {
            try
            {
                await transport.SendJsonAsync<RefreshRequest, object>(
                    "identity", "/internal/auth/logout", HttpMethod.Post, new RefreshRequest(cookie), context);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                // Logout remains best effort; the browser session is cleared below.
            }
        }
        ClearRefreshCookie(context);
        return Results.NoContent();
    }

    private static void SetRefreshCookie(HttpContext context, string refreshToken) =>
        context.Response.Cookies.Append("td_refresh", refreshToken, CookieOptions(context, TimeSpan.FromDays(30)));

    private static void ClearRefreshCookie(HttpContext context) =>
        context.Response.Cookies.Delete("td_refresh", CookieOptions(context, null));

    private static CookieOptions CookieOptions(HttpContext context, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = ProxyHeaders.ShouldUseSecureRefreshCookie(
            context,
            context.RequestServices.GetRequiredService<IHostEnvironment>()),
        IsEssential = true,
        MaxAge = maxAge,
        Path = "/api/auth"
    };
}
