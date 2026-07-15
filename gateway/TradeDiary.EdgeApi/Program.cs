using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production uses HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ProxyHeaders.ConfigureForwardedHeaders(options, builder.Configuration));
builder.Services.AddAuthorization(options =>
{
    var humanOnly = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent")
        .Build();
    options.DefaultPolicy = humanOnly;
    options.FallbackPolicy = humanOnly;
    options.AddPolicy("diaryAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
    {
        if (context.User.FindFirst("account_type")?.Value != "agent") return true;
        var method = (context.Resource as HttpContext)?.Request.Method;
        var requiredScope = method == HttpMethods.Get ? "diary:read" : "diary:write";
        return context.User.FindAll("scope").Any(claim => claim.Value == requiredScope);
    }));
    options.AddPolicy("researchAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
    {
        if (context.User.FindFirst("account_type")?.Value != "agent") return true;
        var request = (context.Resource as HttpContext)?.Request;
        return request?.Method == HttpMethods.Get
            && context.User.FindAll("scope").Any(claim => claim.Value == "research:read");
    }));
    options.AddPolicy("admin", policy => policy.RequireRole("admin"));
});
var serviceDefaults = new Dictionary<string, string>
{
    ["identity"]="http://127.0.0.1:5100", ["journal"]="http://127.0.0.1:5101", ["performance"]="http://127.0.0.1:5102",
    ["discipline"]="http://127.0.0.1:5103", ["reminder"]="http://127.0.0.1:5104", ["stock-research"]="http://127.0.0.1:5105",
    ["market-data"]="http://127.0.0.1:5106", ["price-alert"]="http://127.0.0.1:5107", ["rotation"]="http://127.0.0.1:5108",
    ["partner"]="http://127.0.0.1:5109", ["content"]="http://127.0.0.1:5110", ["tool"]="http://127.0.0.1:5111", ["operations"]="http://127.0.0.1:5112"
};
foreach (var (service, fallback) in serviceDefaults)
{
    var key = string.Concat(service.Split('-').Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    builder.Services.AddHttpClient(service, client => client.BaseAddress = new Uri(builder.Configuration[$"Services:{key}"] ?? fallback));
}
var app = builder.Build();

// Process proxy headers before scheme/host-dependent behavior (auth redirects and cookies).
// ProxyHeaders clears ASP.NET's loopback defaults: forwarded headers are accepted only from
// an explicitly configured reverse proxy.
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    var correlationId = string.IsNullOrWhiteSpace(supplied) ? Guid.NewGuid().ToString() : supplied;
    context.Items["correlationId"] = correlationId; context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
});
app.UseAuthentication(); app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" })).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "edge-api", version = "0.1.0" })).AllowAnonymous();

// ponytail: Edge owns the browser session. The refresh token never reaches JS — login/refresh
// set it as an HttpOnly cookie and return only { accessToken, expiresAt }; refresh/logout read
// the cookie and forward it to Identity (which still owns rotation + family revocation).
app.MapPost("/api/auth/register", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/register") { Content = JsonContent.Create(body) };
    if (context.Request.Headers.TryGetValue("X-Registration-Key", out var key)) request.Headers.TryAddWithoutValidation("X-Registration-Key", key.ToString());
    var response = await clients.CreateClient("identity").SendAsync(request);
    return Results.Content(await response.Content.ReadAsStringAsync(), "application/json", statusCode: (int)response.StatusCode);
}).AllowAnonymous();
app.MapPost("/api/auth/login", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/login") { Content = JsonContent.Create(body) };
    var response = await clients.CreateClient("identity").SendAsync(request);
    if (!response.IsSuccessStatusCode) return Results.Content(await response.Content.ReadAsStringAsync(), "application/json", statusCode: (int)response.StatusCode);
    var tokens = JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject();
    if (tokens == null) return Results.Problem("invalid_token_response", statusCode: 502);
    SetRefreshCookie(context, tokens["refreshToken"]?.GetValue<string>());
    return Results.Ok(new { accessToken = tokens["accessToken"]?.GetValue<string>(), expiresAt = tokens["expiresAt"]?.GetValue<string>() });
}).AllowAnonymous();
app.MapPost("/api/auth/refresh", async (HttpContext context, IHttpClientFactory clients) =>
{
    var cookie = context.Request.Cookies["td_refresh"];
    if (string.IsNullOrEmpty(cookie)) return Results.Unauthorized();
    using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/refresh") { Content = JsonContent.Create(new { refreshToken = cookie }) };
    var response = await clients.CreateClient("identity").SendAsync(request);
    if (!response.IsSuccessStatusCode) { ClearRefreshCookie(context); return Results.Unauthorized(); }
    var tokens = JsonNode.Parse(await response.Content.ReadAsStringAsync())?.AsObject();
    if (tokens == null) { ClearRefreshCookie(context); return Results.Problem("invalid_token_response", statusCode: 502); }
    SetRefreshCookie(context, tokens["refreshToken"]?.GetValue<string>());
    return Results.Ok(new { accessToken = tokens["accessToken"]?.GetValue<string>(), expiresAt = tokens["expiresAt"]?.GetValue<string>() });
}).AllowAnonymous();
app.MapPost("/api/auth/logout", async (HttpContext context, IHttpClientFactory clients) =>
{
    var cookie = context.Request.Cookies["td_refresh"];
    if (!string.IsNullOrEmpty(cookie))
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/logout") { Content = JsonContent.Create(new { refreshToken = cookie }) };
        try { await clients.CreateClient("identity").SendAsync(request); } catch { /* logout is best-effort; clear the cookie regardless */ }
    }
    ClearRefreshCookie(context);
    return Results.NoContent();
}).AllowAnonymous();
app.MapPost("/api/auth/api-key/token", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients, "identity", "/internal/auth/api-key/token", HttpMethod.Post, body, context)).AllowAnonymous();
app.MapPost("/api/app/agents", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients, "identity", "/internal/auth/agents", HttpMethod.Post, body, context));
app.MapDelete("/api/app/api-keys/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory clients) =>
    await ForwardNoBody(clients, "identity", $"/internal/auth/api-keys/{id}", HttpMethod.Delete, context));

app.MapGet("/api/app/dashboard", async (HttpContext context, IHttpClientFactory clients) =>
{
    var result = await CockpitReadModels.ReadDashboardAsync(
        CockpitReadModels.ResolveLocalDate(context.User, DateTimeOffset.UtcNow),
        (service, path) => Send(clients, service, path, context));
    return result.ToHttpResult();
});

app.MapGet("/api/app/calendar", async (int year, int month, HttpContext context, IHttpClientFactory clients) =>
{
    if (!CockpitReadModels.TryCreateCalendarWindow(year, month, out var window))
        return Results.BadRequest(new { error = "invalid_month" });
    var result = await CockpitReadModels.ReadCalendarAsync(
        window,
        (service, path) => Send(clients, service, path, context));
    return result.ToHttpResult();
});

app.MapPost("/api/app/quick-note", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients,"journal","/internal/quick-note",HttpMethod.Post,body,context)).RequireAuthorization("diaryAccess");
app.MapGet("/api/app/diaries", async (HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal","/internal/diaries",HttpMethod.Get,context)).RequireAuthorization("diaryAccess");
app.MapPost("/api/app/diaries", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients,"journal","/internal/diaries",HttpMethod.Post,body,context)).RequireAuthorization("diaryAccess");
app.MapPut("/api/app/diaries/{id:guid}", async (Guid id, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{id}",HttpMethod.Put,body,context)).RequireAuthorization("diaryAccess");
app.MapDelete("/api/app/diaries/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{id}",HttpMethod.Delete,context)).RequireAuthorization("diaryAccess");
app.MapGet("/api/app/diaries/{diaryId:guid}/transactions", async (Guid diaryId, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{diaryId}/transactions",HttpMethod.Get,context)).RequireAuthorization("diaryAccess");
app.MapPost("/api/app/diaries/{diaryId:guid}/transactions", async (Guid diaryId, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{diaryId}/transactions",HttpMethod.Post,body,context)).RequireAuthorization("diaryAccess");
app.MapPut("/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{diaryId}/transactions/{id}",HttpMethod.Put,body,context)).RequireAuthorization("diaryAccess");
app.MapDelete("/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{diaryId}/transactions/{id}",HttpMethod.Delete,context)).RequireAuthorization("diaryAccess");
app.MapPut("/api/app/daily-performance/{date}", async (string date, JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients,"performance",$"/internal/daily-performances/{date}",HttpMethod.Put,body,context));
app.MapGet("/api/app/disciplines", async (HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"discipline","/internal/disciplines",HttpMethod.Get,context));
app.MapPost("/api/app/disciplines", async (JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"discipline","/internal/disciplines",HttpMethod.Post,body,context));
app.MapPut("/api/app/disciplines/{id:guid}", async (Guid id, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"discipline",$"/internal/disciplines/{id}",HttpMethod.Put,body,context));
app.MapDelete("/api/app/disciplines/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"discipline",$"/internal/disciplines/{id}",HttpMethod.Delete,context));
app.MapGet("/api/app/diary-alerts", async (HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"reminder","/internal/diary-alerts",HttpMethod.Get,context));
app.MapPost("/api/app/diary-alerts", async (JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"reminder","/internal/diary-alerts",HttpMethod.Post,body,context));
app.MapPost("/api/app/diary-alerts/{id:guid}/dismiss", async (Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"reminder",$"/internal/diary-alerts/{id}/dismiss",HttpMethod.Post,context));
app.MapDelete("/api/app/diary-alerts/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"reminder",$"/internal/diary-alerts/{id}",HttpMethod.Delete,context));

// Late services stay deliberately thin: Edge owns the public path and transport headers, services own behavior.
MapProxy(app, "/api/app/stocks", "stock-research", "/internal/stocks", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/stocks/{symbol}", "stock-research", "/internal/stocks/{symbol}", [HttpMethods.Get]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/watchlist", "stock-research", "/internal/watchlist", [HttpMethods.Get]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/watchlist/{stockId:guid}", "stock-research", "/internal/watchlist/{stockId}", [HttpMethods.Post, HttpMethods.Delete]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/stocks/{stockId:guid}/note", "stock-research", "/internal/stocks/{stockId}/note", [HttpMethods.Get, HttpMethods.Put]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/stocks/{stockId:guid}/timeline", "stock-research", "/internal/stocks/{stockId}/timeline", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/timeline/{id:guid}", "stock-research", "/internal/timeline/{id}", [HttpMethods.Get]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/timeline/{originalId:guid}/corrections", "stock-research", "/internal/timeline/{originalId}/corrections", [HttpMethods.Post]).RequireAuthorization("researchAccess");
MapProxy(app, "/api/app/market/symbols", "market-data", "/internal/v1/symbols", [HttpMethods.Get]);
MapProxy(app, "/api/app/market/bars/{symbol}", "market-data", "/internal/v1/bars/{symbol}", [HttpMethods.Get]);
MapProxy(app, "/api/app/market/providers/health", "market-data", "/internal/v1/providers/health", [HttpMethods.Get]);
MapProxy(app, "/api/app/price-alerts", "price-alert", "/internal/price-alerts", [HttpMethods.Get, HttpMethods.Post]);
MapProxy(app, "/api/app/price-alerts/{id:guid}", "price-alert", "/internal/price-alerts/{id}", [HttpMethods.Put, HttpMethods.Delete]);
MapProxy(app, "/api/app/price-alerts/{id:guid}/dismiss", "price-alert", "/internal/price-alerts/{id}/dismiss", [HttpMethods.Post]);
MapProxy(app, "/api/app/price-alerts/{id:guid}/reactivate", "price-alert", "/internal/price-alerts/{id}/reactivate", [HttpMethods.Post]);
MapProxy(app, "/api/app/price-alerts/{id:guid}/triggers", "price-alert", "/internal/price-alerts/{id}/triggers", [HttpMethods.Get]);
MapProxy(app, "/api/app/rotation/universes", "rotation", "/internal/rotation/universes", [HttpMethods.Get, HttpMethods.Post]);
MapProxy(app, "/api/app/rotation/universes/{id:guid}", "rotation", "/internal/rotation/universes/{id}", [HttpMethods.Put, HttpMethods.Delete]);
MapProxy(app, "/api/app/rotation/universes/{id:guid}/symbols", "rotation", "/internal/rotation/universes/{id}/symbols", [HttpMethods.Put]);
MapProxy(app, "/api/app/rotation/universes/{id:guid}/calculate", "rotation", "/internal/rotation/universes/{id}/calculate", [HttpMethods.Post]);
MapProxy(app, "/api/app/rotation/monitor", "rotation", "/internal/rotation/monitor", [HttpMethods.Get]);
MapProxy(app, "/api/app/partners", "partner", "/internal/partners", [HttpMethods.Get, HttpMethods.Post]);
MapProxy(app, "/api/app/partners/{id:guid}", "partner", "/internal/partners/{id}", [HttpMethods.Delete]);
MapProxy(app, "/api/app/partners/{id:guid}/accept", "partner", "/internal/partners/{id}/accept", [HttpMethods.Post]);
MapProxy(app, "/api/app/partners/{id:guid}/share-policy", "partner", "/internal/partners/{id}/share-policy", [HttpMethods.Put]);
MapProxy(app, "/api/app/partners/{ownerId:guid}/authorization", "partner", "/internal/partners/{ownerId}/authorization", [HttpMethods.Get]);
MapProxy(app, "/api/app/tools/position-sizing", "tool", "/internal/tools/position-sizing", [HttpMethods.Post]);
MapProxy(app, "/api/app/tools/risk-reward", "tool", "/internal/tools/risk-reward", [HttpMethods.Post]);
MapProxy(app, "/api/app/tools/fire", "tool", "/internal/tools/fire", [HttpMethods.Post]);
MapProxy(app, "/api/app/tools/relative-value", "tool", "/internal/tools/relative-value", [HttpMethods.Post]);
MapProxy(app, "/api/app/tools/seasonality", "tool", "/internal/tools/seasonality", [HttpMethods.Post]);
MapProxy(app, "/api/admin/posts", "content", "/internal/admin/posts", [HttpMethods.Post]).RequireAuthorization("admin");
MapProxy(app, "/api/admin/posts/{id:guid}", "content", "/internal/admin/posts/{id}", [HttpMethods.Put, HttpMethods.Delete]).RequireAuthorization("admin");
MapProxy(app, "/api/admin/operations/audit", "operations", "/internal/operations/audit", [HttpMethods.Get]).RequireAuthorization("admin");
MapProxy(app, "/api/admin/operations/jobs", "operations", "/internal/operations/jobs", [HttpMethods.Get, HttpMethods.Post]).RequireAuthorization("admin");
MapProxy(app, "/api/admin/operations/health", "operations", "/internal/operations/health", [HttpMethods.Post]).RequireAuthorization("admin");
MapProxy(app, "/api/content/posts", "content", "/internal/posts", [HttpMethods.Get]).AllowAnonymous();
MapProxy(app, "/api/content/posts/{slug}", "content", "/internal/posts/{slug}", [HttpMethods.Get]).AllowAnonymous();

app.MapGet("/api/app/stocks/{symbol}/page", async (string symbol, HttpContext context, IHttpClientFactory clients) =>
{
    var stockTask = Send(clients, "stock-research", $"/internal/stocks/{Uri.EscapeDataString(symbol)}", context);
    var barsTask = Send(clients, "market-data", $"/internal/v1/bars/{Uri.EscapeDataString(symbol)}{context.Request.QueryString}", context);
    await Task.WhenAll(stockTask, barsTask);
    if (stockTask.Result.StatusCode != 200) return Results.Content(stockTask.Result.Body?.ToJsonString() ?? "", "application/json", statusCode: stockTask.Result.StatusCode);
    return Results.Ok(new JsonObject { ["stock"] = stockTask.Result.Body?.DeepClone(), ["bars"] = barsTask.Result.StatusCode == 200 ? barsTask.Result.Body?.DeepClone() : null, ["capabilities"] = new JsonObject { ["marketData"] = barsTask.Result.StatusCode == 200 ? "available" : "unavailable" } });
}).RequireAuthorization("researchAccess");

app.Run();

static RouteHandlerBuilder MapProxy(WebApplication app, string route, string service, string target, string[] methods) =>
    app.MapMethods(route, methods, async (HttpContext context, IHttpClientFactory clients) => await Proxy(clients, service, Expand(target, context.Request.RouteValues) + context.Request.QueryString, context));
static string Expand(string path, RouteValueDictionary values)
{
    foreach (var (key, value) in values) path = path.Replace($"{{{key}}}", Uri.EscapeDataString(value?.ToString() ?? ""), StringComparison.Ordinal);
    return path;
}
static async Task<IResult> Proxy(IHttpClientFactory clients, string service, string path, HttpContext context)
{
    try
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content = new StreamContent(context.Request.Body);
        if (request.Content is not null && context.Request.ContentType is not null) request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        ForwardHeaders(request, context);
        using var response = await clients.CreateClient(service).SendAsync(request); var body = await response.Content.ReadAsStringAsync();
        PropagateHeaders(context, response);
        return Results.Content(body, response.Content.Headers.ContentType?.MediaType ?? "application/json", statusCode: (int)response.StatusCode);
    }
    catch { return Results.Problem("Service unavailable.", statusCode: 503); }
}

// ponytail: Edge ferries transport headers only — Authorization + correlation always; Idempotency-Key
// passes through so Journal's idempotency layer applies across the Edge hop; Location comes back so
// created resources keep their address. Services own behavior, Edge owns transport.
static void ForwardHeaders(HttpRequestMessage request, HttpContext context)
{
    ProxyHeaders.Forward(request, context);
}
static void PropagateHeaders(HttpContext context, HttpResponseMessage response)
{
    ProxyHeaders.Propagate(context, response);
}

// ponytail: HttpOnly + SameSite=Lax + Secure(when HTTPS). Path scoped to /api/auth so the cookie
// only travels on refresh/logout. Identity owns rotation; Edge only ferries the value.
static void SetRefreshCookie(HttpContext context, string? refreshToken)
{
    if (string.IsNullOrEmpty(refreshToken)) { ClearRefreshCookie(context); return; }
    context.Response.Cookies.Append("td_refresh", refreshToken, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = ProxyHeaders.ShouldUseSecureRefreshCookie(context, context.RequestServices.GetRequiredService<IHostEnvironment>()),
        IsEssential = true,
        MaxAge = TimeSpan.FromDays(30),
        Path = "/api/auth"
    });
}
static void ClearRefreshCookie(HttpContext context) =>
    context.Response.Cookies.Delete("td_refresh", new CookieOptions
    {
        Path = "/api/auth",
        Secure = ProxyHeaders.ShouldUseSecureRefreshCookie(context, context.RequestServices.GetRequiredService<IHostEnvironment>())
    });

static async Task<CockpitReadModels.DownstreamResponse> Send(IHttpClientFactory clients, string service, string path, HttpContext context)
{
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,path);
        request.Headers.TryAddWithoutValidation("Authorization",context.Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Correlation-ID",context.Items["correlationId"]?.ToString());
        using var response = await clients.CreateClient(service).SendAsync(request);
        var text = await response.Content.ReadAsStringAsync(); return new((int)response.StatusCode,string.IsNullOrWhiteSpace(text)?null:JsonNode.Parse(text));
    }
    catch { return new(503,null); }
}
static async Task<IResult> Forward(IHttpClientFactory clients, string service, string path, HttpMethod method, JsonElement body, HttpContext context)
{
    try
    {
        using var request = new HttpRequestMessage(method,path) { Content = JsonContent.Create(body) };
        ForwardHeaders(request, context);
        using var response = await clients.CreateClient(service).SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        PropagateHeaders(context, response);
        return Results.Content(text,string.IsNullOrWhiteSpace(response.Content.Headers.ContentType?.MediaType)?"application/json":response.Content.Headers.ContentType.MediaType,statusCode:(int)response.StatusCode);
    }
    catch { return Results.Problem("Service unavailable.",statusCode:503); }
}
static async Task<IResult> ForwardNoBody(IHttpClientFactory clients, string service, string path, HttpMethod method, HttpContext context)
{
    try
    {
        using var request = new HttpRequestMessage(method,path);
        ForwardHeaders(request, context);
        using var response = await clients.CreateClient(service).SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        PropagateHeaders(context, response);
        return Results.Content(text,string.IsNullOrWhiteSpace(response.Content.Headers.ContentType?.MediaType)?"application/json":response.Content.Headers.ContentType.MediaType,statusCode:(int)response.StatusCode);
    }
    catch { return Results.Problem("Service unavailable.",statusCode:503); }
}
