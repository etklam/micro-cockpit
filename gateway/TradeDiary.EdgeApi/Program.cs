using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production uses HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
foreach (var service in new[] { "identity", "journal", "performance", "discipline", "reminder" })
{
    var fallback = service switch { "identity" => "http://127.0.0.1:5100", "journal" => "http://127.0.0.1:5101", "performance" => "http://127.0.0.1:5102", "discipline" => "http://127.0.0.1:5103", _ => "http://127.0.0.1:5104" };
    builder.Services.AddHttpClient(service, client => client.BaseAddress = new Uri(builder.Configuration[$"Services:{service}"] ?? fallback));
}
var app = builder.Build();

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

foreach (var action in new[] { "register", "login", "refresh", "logout" })
{
    app.MapPost($"/api/auth/{action}", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/auth/{action}") { Content = JsonContent.Create(body) };
        if (context.Request.Headers.TryGetValue("X-Registration-Key", out var key)) request.Headers.TryAddWithoutValidation("X-Registration-Key", key.ToString());
        var response = await clients.CreateClient("identity").SendAsync(request); var content = await response.Content.ReadAsStringAsync();
        return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
    }).AllowAnonymous();
}

app.MapGet("/api/app/dashboard", async (HttpContext context, IHttpClientFactory clients) =>
{
    var localDate = UserLocalDate(context.User);
    var from = localDate.ToString("yyyy-MM-dd");
    var calls = new[]
    {
        Send(clients,"journal",$"/internal/diary-day-summary?from={from}&to={from}",context),
        Send(clients,"journal","/internal/diaries",context),
        Send(clients,"performance",$"/internal/performance/day/{from}",context),
        Send(clients,"discipline",$"/internal/disciplines/today?date={from}",context),
        Send(clients,"reminder",$"/internal/diary-alerts/day-summary?date={from}",context)
    };
    await Task.WhenAll(calls); var journalDay = calls[0].Result; var diaries = calls[1].Result; var performance = calls[2].Result; var discipline = calls[3].Result; var alerts = calls[4].Result;
    if (journalDay.Status != 200 || diaries.Status != 200 || performance.Status is not (200 or 404)) return Results.Problem("Required dashboard service unavailable.", statusCode: 503);
    if (discipline.Status is 401 or 403 || alerts.Status is 401 or 403) return Results.Problem("Downstream authorization failed.", statusCode: 503);
    var day = journalDay.Body?["items"]?.AsArray().FirstOrDefault();
    var recent = new JsonArray(diaries.Body?["items"]?.AsArray().Take(5).Select(x => x?.DeepClone()).ToArray() ?? []);
    return Results.Ok(new JsonObject
    {
        ["localDate"] = from,
        ["diary"] = new JsonObject { ["writtenToday"] = (day?["diaryCount"]?.GetValue<long>() ?? 0) > 0, ["count"] = day?["diaryCount"]?.DeepClone() ?? 0 },
        ["performance"] = performance.Status == 404 ? null : performance.Body?.DeepClone(),
        ["pendingAlerts"] = alerts.Status == 200 ? alerts.Body?["count"]?.DeepClone() : null,
        ["discipline"] = discipline.Status == 200 ? discipline.Body?.DeepClone() : null,
        ["recentDiaries"] = recent,
        ["capabilities"] = new JsonObject { ["alerts"] = alerts.Status == 200 ? "available" : "unavailable", ["discipline"] = discipline.Status == 200 ? "available" : discipline.Status == 404 ? "empty" : "unavailable" }
    });
});

app.MapGet("/api/app/calendar", async (int year, int month, HttpContext context, IHttpClientFactory clients) =>
{
    if (month is < 1 or > 12) return Results.BadRequest(new { error = "invalid_month" });
    var start = new DateOnly(year, month, 1); var end = start.AddMonths(1).AddDays(-1);
    var from = start.ToString("yyyy-MM-dd"); var to = end.ToString("yyyy-MM-dd");
    var journalTask = Send(clients,"journal",$"/internal/diary-day-summary?from={from}&to={to}",context);
    var performanceTask = Send(clients,"performance",$"/internal/daily-performances?from={from}&to={to}",context);
    var summaryTask = Send(clients,"performance",$"/internal/performance/month-summary?year={year}&month={month}",context);
    var alertTask = Send(clients,"reminder",$"/internal/diary-alerts/day-summaries?from={from}&to={to}",context);
    await Task.WhenAll(journalTask,performanceTask,summaryTask,alertTask);
    var journal = journalTask.Result; var performance = performanceTask.Result; var summary = summaryTask.Result; var alerts = alertTask.Result;
    if (journal.Status != 200 || performance.Status != 200 || summary.Status != 200) return Results.Problem("Required calendar service unavailable.", statusCode: 503);
    if (alerts.Status is 401 or 403) return Results.Problem("Downstream authorization failed.", statusCode: 503);
    var journalByDate = Index(journal.Body?["items"]); var performanceByDate = Index(performance.Body?["items"]); var alertByDate = Index(alerts.Body?["items"]);
    var days = new JsonArray();
    for (var date = start; date <= end; date = date.AddDays(1))
    {
        var key = date.ToString("yyyy-MM-dd"); journalByDate.TryGetValue(key, out var journalItem); performanceByDate.TryGetValue(key, out var performanceItem); alertByDate.TryGetValue(key, out var alertItem);
        days.Add(new JsonObject { ["date"] = key, ["performance"] = performanceItem?.DeepClone(), ["diaryCount"] = journalItem?["diaryCount"]?.DeepClone() ?? 0, ["transactionCount"] = journalItem?["transactionCount"]?.DeepClone() ?? 0, ["alertCount"] = alerts.Status == 200 ? alertItem?["count"]?.DeepClone() ?? 0 : null });
    }
    return Results.Ok(new JsonObject { ["year"] = year, ["month"] = month, ["summary"] = summary.Body?.DeepClone(), ["days"] = days, ["capabilities"] = new JsonObject { ["alerts"] = alerts.Status == 200 ? "available" : "unavailable" } });
});

app.MapPost("/api/app/quick-note", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients,"journal","/internal/quick-note",HttpMethod.Post,body,context));
app.MapGet("/api/app/diaries", async (HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal","/internal/diaries",HttpMethod.Get,context));
app.MapPost("/api/app/diaries", async (JsonElement body, HttpContext context, IHttpClientFactory clients) =>
    await Forward(clients,"journal","/internal/diaries",HttpMethod.Post,body,context));
app.MapPut("/api/app/diaries/{id:guid}", async (Guid id, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{id}",HttpMethod.Put,body,context));
app.MapDelete("/api/app/diaries/{id:guid}", async (Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{id}",HttpMethod.Delete,context));
app.MapGet("/api/app/diaries/{diaryId:guid}/transactions", async (Guid diaryId, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{diaryId}/transactions",HttpMethod.Get,context));
app.MapPost("/api/app/diaries/{diaryId:guid}/transactions", async (Guid diaryId, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{diaryId}/transactions",HttpMethod.Post,body,context));
app.MapPut("/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, JsonElement body, HttpContext context, IHttpClientFactory clients) => await Forward(clients,"journal",$"/internal/diaries/{diaryId}/transactions/{id}",HttpMethod.Put,body,context));
app.MapDelete("/api/app/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, HttpContext context, IHttpClientFactory clients) => await ForwardNoBody(clients,"journal",$"/internal/diaries/{diaryId}/transactions/{id}",HttpMethod.Delete,context));
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

app.Run();

static DateOnly UserLocalDate(System.Security.Claims.ClaimsPrincipal user)
{
    var id = user.FindFirst("timezone")?.Value ?? "UTC"; TimeZoneInfo timezone;
    try { timezone = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { timezone = TimeZoneInfo.Utc; }
    return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
}
static async Task<ServiceResult> Send(IHttpClientFactory clients, string service, string path, HttpContext context)
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
        request.Headers.TryAddWithoutValidation("Authorization",context.Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Correlation-ID",context.Items["correlationId"]?.ToString());
        using var response = await clients.CreateClient(service).SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        return Results.Content(text,string.IsNullOrWhiteSpace(response.Content.Headers.ContentType?.MediaType)?"application/json":response.Content.Headers.ContentType.MediaType,statusCode:(int)response.StatusCode);
    }
    catch { return Results.Problem("Service unavailable.",statusCode:503); }
}
static async Task<IResult> ForwardNoBody(IHttpClientFactory clients, string service, string path, HttpMethod method, HttpContext context)
{
    try
    {
        using var request = new HttpRequestMessage(method,path);
        request.Headers.TryAddWithoutValidation("Authorization",context.Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Correlation-ID",context.Items["correlationId"]?.ToString());
        using var response = await clients.CreateClient(service).SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        return Results.Content(text,string.IsNullOrWhiteSpace(response.Content.Headers.ContentType?.MediaType)?"application/json":response.Content.Headers.ContentType.MediaType,statusCode:(int)response.StatusCode);
    }
    catch { return Results.Problem("Service unavailable.",statusCode:503); }
}
static Dictionary<string,JsonNode> Index(JsonNode? node) => node?.AsArray().Where(x => x?["localDate"] is not null).ToDictionary(x => x!["localDate"]!.GetValue<string>(),x => x!) ?? [];
record ServiceResult(int Status, JsonNode? Body);
