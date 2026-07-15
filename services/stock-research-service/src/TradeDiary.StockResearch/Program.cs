using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("StockResearch") ?? "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options =>
{
    var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build();
    options.DefaultPolicy = humanOnly;
    options.FallbackPolicy = humanOnly;
    options.AddPolicy("researchAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
    {
        if (context.User.FindFirst("account_type")?.Value != "agent") return true;
        var request = (context.Resource as HttpContext)?.Request;
        return request?.Method == HttpMethods.Get
            && context.User.FindAll("scope").Any(claim => claim.Value == "research:read");
    }));
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "stock-research-service", version = "0.1.0" })).AllowAnonymous();

var research = app.MapGroup("/internal").RequireAuthorization("researchAccess");

research.MapGet("/stocks", async (string? query, NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("SELECT id,symbol,name,exchange,asset_type,created_at FROM stock_research.stocks WHERE $1='' OR symbol ILIKE '%'||$1||'%' OR name ILIKE '%'||$1||'%' ORDER BY symbol LIMIT 100");
    command.Parameters.AddWithValue(query?.Trim() ?? "");
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<StockResponse>();
    while (await reader.ReadAsync()) items.Add(ReadStock(reader));
    return Results.Ok(new CollectionResponse<StockResponse>(items));
})
.Produces<CollectionResponse<StockResponse>>(200).ProducesProblem(401);

research.MapGet("/stocks/{symbol}", async Task<IResult> (string symbol, NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("SELECT id,symbol,name,exchange,asset_type,created_at FROM stock_research.stocks WHERE symbol=$1");
    command.Parameters.AddWithValue(symbol.Trim().ToUpperInvariant()); await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadStock(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<StockResponse>(200).ProducesProblem(401).ProducesProblem(404);

research.MapPost("/stocks", async Task<IResult> (StockWrite input, NpgsqlDataSource db) =>
{
    var symbol = input.Symbol?.Trim().ToUpperInvariant() ?? ""; var name = input.Name?.Trim() ?? ""; var exchange = input.Exchange?.Trim() ?? "";
    if (symbol.Length is 0 or > 20 || name.Length is 0 or > 200) return Results.Problem("symbol_and_name_required", statusCode: 400);
    if (!string.Equals(input.AssetType ?? "stock", "stock", StringComparison.OrdinalIgnoreCase)) return Results.Problem("stocks_only", statusCode: 400);
    var id = Guid.NewGuid();
    await using var command = db.CreateCommand("INSERT INTO stock_research.stocks(id,symbol,name,exchange) VALUES($1,$2,$3,$4) RETURNING id,symbol,name,exchange,asset_type,created_at");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(symbol); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(exchange);
    try { await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync(); return Results.Created($"/internal/stocks/{symbol}", ReadStock(reader)); }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return Results.Problem("symbol_exists", statusCode: 409); }
})
.Produces<StockResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

research.MapGet("/watchlist", async Task<IResult> (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT s.id,s.symbol,s.name,s.exchange,s.asset_type,s.created_at,n.content,n.updated_at,
               (SELECT count(*) FROM stock_research.stock_timeline_records t WHERE t.user_id=$1 AND t.stock_id=s.id)
        FROM stock_research.watchlist_items w JOIN stock_research.stocks s ON s.id=w.stock_id
        LEFT JOIN stock_research.stock_notes n ON n.user_id=w.user_id AND n.stock_id=w.stock_id
        WHERE w.user_id=$1 ORDER BY s.symbol
        """);
    command.Parameters.AddWithValue(userId); await using var reader = await command.ExecuteReaderAsync(); var items = new List<WatchlistResponse>();
    while (await reader.ReadAsync()) items.Add(new(ReadStock(reader), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetDateTime(7), reader.GetInt64(8)));
    return Results.Ok(new CollectionResponse<WatchlistResponse>(items));
})
.Produces<CollectionResponse<WatchlistResponse>>(200).ProducesProblem(401);

research.MapPost("/watchlist/{stockId:guid}", async Task<IResult> (Guid stockId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("INSERT INTO stock_research.watchlist_items(user_id,stock_id) SELECT $1,id FROM stock_research.stocks WHERE id=$2 ON CONFLICT DO NOTHING RETURNING stock_id");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId); var result = await command.ExecuteScalarAsync();
    if (result is not null) return Results.Created($"/internal/watchlist/{stockId}", new WatchlistItemCreatedResponse(stockId));
    await using var exists = db.CreateCommand("SELECT EXISTS(SELECT 1 FROM stock_research.stocks WHERE id=$1)"); exists.Parameters.AddWithValue(stockId);
    return (bool)(await exists.ExecuteScalarAsync() ?? false) ? Results.NoContent() : Results.Problem("not_found", statusCode: 404);
})
.Produces<WatchlistItemCreatedResponse>(201).Produces(204).ProducesProblem(401).ProducesProblem(404);

research.MapDelete("/watchlist/{stockId:guid}", async Task<IResult> (Guid stockId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("DELETE FROM stock_research.watchlist_items WHERE user_id=$1 AND stock_id=$2");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

research.MapGet("/stocks/{stockId:guid}/note", async Task<IResult> (Guid stockId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT stock_id,content,created_at,updated_at FROM stock_research.stock_notes WHERE user_id=$1 AND stock_id=$2");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId); await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadNote(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<NoteResponse>(200).ProducesProblem(401).ProducesProblem(404);

research.MapPut("/stocks/{stockId:guid}/note", async Task<IResult> (Guid stockId, NoteWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized(); var content = input.Content?.Trim() ?? "";
    await using var command = db.CreateCommand("""
        INSERT INTO stock_research.stock_notes(user_id,stock_id,content)
        SELECT $1,id,$3 FROM stock_research.stocks WHERE id=$2
        ON CONFLICT(user_id,stock_id) DO UPDATE SET content=excluded.content,updated_at=now()
        RETURNING stock_id,content,created_at,updated_at
        """);
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId); command.Parameters.AddWithValue(content);
    await using var reader = await command.ExecuteReaderAsync(); return await reader.ReadAsync() ? Results.Ok(ReadNote(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<NoteResponse>(200).ProducesProblem(401).ProducesProblem(404);

research.MapGet("/stocks/{stockId:guid}/timeline", async Task<IResult> (Guid stockId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var exists = db.CreateCommand("SELECT EXISTS(SELECT 1 FROM stock_research.stocks WHERE id=$1)"); exists.Parameters.AddWithValue(stockId);
    if (!(bool)(await exists.ExecuteScalarAsync() ?? false)) return Results.Problem("not_found", statusCode: 404);
    await using var command = db.CreateCommand("SELECT id,stock_id,event_time,source_type,title,content,diary_id,correction_of_id,created_at FROM stock_research.stock_timeline_records WHERE user_id=$1 AND stock_id=$2 ORDER BY event_time DESC,created_at DESC,id");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId); await using var reader = await command.ExecuteReaderAsync(); var items = new List<TimelineResponse>();
    while (await reader.ReadAsync()) items.Add(ReadTimeline(reader)); return Results.Ok(new CollectionResponse<TimelineResponse>(items));
})
.Produces<CollectionResponse<TimelineResponse>>(200).ProducesProblem(401).ProducesProblem(404);

research.MapGet("/timeline/{id:guid}", async Task<IResult> (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT id,stock_id,event_time,source_type,title,content,diary_id,correction_of_id,created_at FROM stock_research.stock_timeline_records WHERE id=$1 AND user_id=$2");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadTimeline(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<TimelineResponse>(200).ProducesProblem(401).ProducesProblem(404);

research.MapPost("/stocks/{stockId:guid}/timeline", async Task<IResult> (Guid stockId, TimelineWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    return await AppendTimeline(db, userId, stockId, null, input);
})
.Produces<TimelineResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

research.MapPost("/timeline/{originalId:guid}/corrections", async Task<IResult> (Guid originalId, TimelineWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT stock_id FROM stock_research.stock_timeline_records WHERE id=$1 AND user_id=$2");
    command.Parameters.AddWithValue(originalId); command.Parameters.AddWithValue(userId); var stockId = await command.ExecuteScalarAsync();
    return stockId is Guid id ? await AppendTimeline(db, userId, id, originalId, input) : Results.Problem("not_found", statusCode: 404);
})
.Produces<TimelineResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) => Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
static StockResponse ReadStock(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5));
static NoteResponse ReadNote(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetDateTime(2), r.GetDateTime(3));
static TimelineResponse ReadTimeline(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetDateTime(2), r.GetString(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetGuid(6), r.IsDBNull(7) ? null : r.GetGuid(7), r.GetDateTime(8));
static async Task<IResult> AppendTimeline(NpgsqlDataSource db, Guid userId, Guid stockId, Guid? correctionOfId, TimelineWrite input)
{
    var sourceType = input.SourceType?.Trim() ?? ""; var title = input.Title?.Trim() ?? ""; var content = input.Content?.Trim() ?? "";
    if (sourceType.Length == 0 || title.Length == 0 || content.Length == 0) return Results.Problem("source_type_title_and_content_required", statusCode: 400);
    var id = Guid.NewGuid(); var eventTime = input.EventTime ?? DateTimeOffset.UtcNow;
    await using var command = db.CreateCommand("""
        INSERT INTO stock_research.stock_timeline_records(id,user_id,stock_id,event_time,source_type,title,content,diary_id,correction_of_id)
        SELECT $1,$2,id,$4,$5,$6,$7,$8,$9 FROM stock_research.stocks WHERE id=$3
        RETURNING id,stock_id,event_time,source_type,title,content,diary_id,correction_of_id,created_at
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(stockId); command.Parameters.AddWithValue(eventTime);
    command.Parameters.AddWithValue(sourceType); command.Parameters.AddWithValue(title); command.Parameters.AddWithValue(content);
    command.Parameters.AddWithValue(input.DiaryId is Guid diaryId ? diaryId : DBNull.Value); command.Parameters.AddWithValue(correctionOfId is Guid correctionId ? correctionId : DBNull.Value);
    await using var reader = await command.ExecuteReaderAsync(); return await reader.ReadAsync() ? Results.Created($"/internal/timeline/{id}", ReadTimeline(reader)) : Results.Problem("not_found", statusCode: 404);
}

record StockWrite(string? Symbol, string? Name, string? Exchange, string? AssetType);
record NoteWrite(string? Content);
record TimelineWrite(DateTimeOffset? EventTime, string? SourceType, string? Title, string? Content, Guid? DiaryId);
record StockResponse(Guid Id, string Symbol, string Name, string Exchange, string AssetType, DateTime CreatedAt);
record WatchlistResponse(StockResponse Stock, string? CurrentNote, DateTime? NoteUpdatedAt, long TimelineCount);
record WatchlistItemCreatedResponse(Guid StockId);
record NoteResponse(Guid StockId, string Content, DateTime CreatedAt, DateTime UpdatedAt);
record TimelineResponse(Guid Id, Guid StockId, DateTime EventTime, string SourceType, string Title, string Content, Guid? DiaryId, Guid? CorrectionOfId, DateTime CreatedAt);
record CollectionResponse<T>(List<T> Items);

// ponytail: shared OpenAPI security wiring — bearerAuth for user routes, serviceKey for internal admin/worker/events.
// Duplicated per service intentionally: no shared kernel is allowed across services.
sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
        document.Components.SecuritySchemes["serviceKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Service-Key"
        };
        return Task.CompletedTask;
    }
}

sealed class SecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<AllowAnonymousAttribute>().Any()) return Task.CompletedTask;
        var scheme = metadata.OfType<IAuthorizeData>().Any(data => data.Policy == "serviceKey")
            ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}
