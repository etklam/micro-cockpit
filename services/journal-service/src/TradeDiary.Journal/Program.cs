using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Journal") ??
    "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddHttpClient("reminder", client => client.BaseAddress = new Uri(builder.Configuration["Services:Reminder"] ?? "http://127.0.0.1:5104"));
builder.Services.AddHostedService<OutboxPublisher>();
var app = builder.Build();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.FindFirst("account_type")?.Value == "agent" && context.Request.Path.StartsWithSegments("/internal"))
    {
        var required = context.Request.Method == HttpMethods.Get ? "diary:read" : "diary:write";
        if (!context.User.FindAll("scope").Any(claim => claim.Value == required)) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    }
    await next();
});
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); }
    catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); }
}).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "journal-service", version = "0.1.0" })).AllowAnonymous();

app.MapGet("/internal/diaries", async (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT id, local_date, title, content, created_at, updated_at
        FROM journal.diaries WHERE user_id = $1 AND deleted_at IS NULL
        ORDER BY local_date DESC, created_at DESC
        """);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<DiaryResponse>();
    while (await reader.ReadAsync()) items.Add(ReadDiary(reader));
    return Results.Ok(new { items });
});

app.MapGet("/internal/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT id, local_date, title, content, created_at, updated_at
        FROM journal.diaries WHERE id = $1 AND user_id = $2 AND deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadDiary(reader)) : Results.NotFound();
});

app.MapGet("/internal/diary-day-summary", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (to < from || to.DayNumber - from.DayNumber > 62) return Results.BadRequest(new { error = "invalid_date_range" });
    var timezone = request.HttpContext.User.FindFirst("timezone")?.Value ?? "UTC";
    await using var command = db.CreateCommand("""
        WITH diary_counts AS (
          SELECT local_date, count(*) diary_count FROM journal.diaries
          WHERE user_id=$1 AND deleted_at IS NULL AND local_date BETWEEN $2 AND $3 GROUP BY local_date
        ), transaction_counts AS (
          SELECT (traded_at AT TIME ZONE $4)::date local_date, count(*) transaction_count FROM journal.transactions
          WHERE user_id=$1 AND deleted_at IS NULL AND (traded_at AT TIME ZONE $4)::date BETWEEN $2 AND $3 GROUP BY 1
        )
        SELECT coalesce(d.local_date,t.local_date),coalesce(d.diary_count,0),coalesce(t.transaction_count,0)
        FROM diary_counts d FULL JOIN transaction_counts t USING(local_date) ORDER BY 1
        """);
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to); command.Parameters.AddWithValue(timezone);
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<object>();
    while (await reader.ReadAsync()) items.Add(new { localDate = reader.GetFieldValue<DateOnly>(0), diaryCount = reader.GetInt64(1), transactionCount = reader.GetInt64(2) });
    return Results.Ok(new { items });
});

app.MapPost("/internal/diaries", async (DiaryWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "title_required" });
    var id = Guid.NewGuid();
    await using var command = db.CreateCommand("""
        INSERT INTO journal.diaries (id, user_id, local_date, title, content)
        VALUES ($1, $2, $3, $4, $5)
        RETURNING created_at, updated_at
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.LocalDate);
    command.Parameters.AddWithValue(input.Title.Trim());
    command.Parameters.AddWithValue(input.Content ?? "");
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    var result = new DiaryResponse(id, input.LocalDate, input.Title.Trim(), input.Content ?? "", reader.GetDateTime(0), reader.GetDateTime(1));
    return Results.Created($"/internal/diaries/{id}", result);
});

app.MapPut("/internal/diaries/{id:guid}", async (Guid id, DiaryWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "title_required" });
    await using var command = db.CreateCommand("""
        UPDATE journal.diaries SET local_date=$3, title=$4, content=$5, updated_at=now()
        WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.LocalDate);
    command.Parameters.AddWithValue(input.Title.Trim());
    command.Parameters.AddWithValue(input.Content ?? "");
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/internal/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand("UPDATE journal.diaries SET deleted_at=now(),updated_at=now() WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL", connection, tx);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
    await using var outbox = new NpgsqlCommand("INSERT INTO journal.outbox_events(event_id,event_type,event_version,payload) VALUES($1,'DiaryDeleted.v1',1,jsonb_build_object('diaryId',$2,'userId',$3))", connection, tx);
    outbox.Parameters.AddWithValue(Guid.NewGuid()); outbox.Parameters.AddWithValue(id); outbox.Parameters.AddWithValue(userId);
    await outbox.ExecuteNonQueryAsync(); await tx.CommitAsync();
    return Results.NoContent();
});

app.MapPost("/internal/quick-note", async (QuickNote input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Content)) return Results.BadRequest(new { error = "content_required" });
    if (input.TargetDiaryId is { } target)
    {
        await using var append = db.CreateCommand("""
            UPDATE journal.diaries SET content = concat_ws(E'\n\n', nullif(content, ''), $3), updated_at=now()
            WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
            """);
        append.Parameters.AddWithValue(target);
        append.Parameters.AddWithValue(userId);
        append.Parameters.AddWithValue(input.Content.Trim());
        return await append.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.Ok(new { diaryId = target, appended = true });
    }

    var id = Guid.NewGuid();
    await using var create = db.CreateCommand("INSERT INTO journal.diaries (id,user_id,local_date,title,content) VALUES ($1,$2,$3,'Quick note',$4)");
    create.Parameters.AddWithValue(id);
    create.Parameters.AddWithValue(userId);
    create.Parameters.AddWithValue(input.LocalDate);
    create.Parameters.AddWithValue(input.Content.Trim());
    await create.ExecuteNonQueryAsync();
    return Results.Created($"/internal/diaries/{id}", new { diaryId = id, appended = false });
});

app.MapGet("/internal/diaries/{diaryId:guid}/transactions", async (Guid diaryId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (!await OwnsDiary(db, diaryId, userId)) return Results.NotFound();
    await using var command = db.CreateCommand("""
        SELECT id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
        FROM journal.transactions WHERE diary_id=$1 AND user_id=$2 AND deleted_at IS NULL ORDER BY traded_at DESC
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<TransactionResponse>();
    while (await reader.ReadAsync()) items.Add(ReadTransaction(reader));
    return Results.Ok(new { items });
});

app.MapPost("/internal/diaries/{diaryId:guid}/transactions", async (Guid diaryId, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var error = ValidateTransaction(input);
    if (error is not null) return Results.BadRequest(new { error });
    var id = Guid.NewGuid();
    await using var command = db.CreateCommand("""
        INSERT INTO journal.transactions (id,diary_id,user_id,symbol,side,quantity,price,currency,traded_at,notes)
        SELECT $1,id,$2,$3,$4,$5,$6,$7,$8,$9 FROM journal.diaries
        WHERE id=$10 AND user_id=$2 AND deleted_at IS NULL
        RETURNING id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
        """);
    AddTransactionParameters(command, id, userId, diaryId, input);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? Results.Created($"/internal/diaries/{diaryId}/transactions/{id}", ReadTransaction(reader))
        : Results.NotFound();
});

app.MapPut("/internal/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var error = ValidateTransaction(input);
    if (error is not null) return Results.BadRequest(new { error });
    await using var command = db.CreateCommand("""
        UPDATE journal.transactions SET symbol=$4,side=$5,quantity=$6,price=$7,currency=$8,traded_at=$9,notes=$10,updated_at=now()
        WHERE id=$1 AND diary_id=$2 AND user_id=$3 AND deleted_at IS NULL
          AND EXISTS (SELECT 1 FROM journal.diaries d WHERE d.id=$2 AND d.user_id=$3 AND d.deleted_at IS NULL)
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.Symbol.Trim().ToUpperInvariant()); command.Parameters.AddWithValue(input.Side.ToLowerInvariant());
    command.Parameters.AddWithValue(input.Quantity); command.Parameters.AddWithValue(input.Price);
    command.Parameters.AddWithValue(input.Currency.Trim().ToUpperInvariant()); command.Parameters.AddWithValue(input.TradedAt.ToUniversalTime());
    command.Parameters.AddWithValue(input.Notes ?? "");
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/internal/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        UPDATE journal.transactions SET deleted_at=now(),updated_at=now()
        WHERE id=$1 AND diary_id=$2 AND user_id=$3 AND deleted_at IS NULL
          AND EXISTS (SELECT 1 FROM journal.diaries d WHERE d.id=$2 AND d.user_id=$3 AND d.deleted_at IS NULL)
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) =>
    Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

static DiaryResponse ReadDiary(NpgsqlDataReader reader) => new(
    reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetString(3),
    reader.GetDateTime(4), reader.GetDateTime(5));

static async Task<bool> OwnsDiary(NpgsqlDataSource db, Guid diaryId, Guid userId)
{
    await using var command = db.CreateCommand("SELECT 1 FROM journal.diaries WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL");
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    return await command.ExecuteScalarAsync() is not null;
}

static string? ValidateTransaction(TransactionWrite input)
{
    if (string.IsNullOrWhiteSpace(input.Symbol)) return "symbol_required";
    if (input.Side.ToLowerInvariant() is not ("buy" or "sell")) return "invalid_side";
    if (input.Quantity <= 0 || input.Price <= 0) return "quantity_and_price_must_be_positive";
    if (input.Currency.Trim().Length != 3 || !input.Currency.All(char.IsLetter)) return "invalid_currency";
    return null;
}

static void AddTransactionParameters(NpgsqlCommand command, Guid id, Guid userId, Guid diaryId, TransactionWrite input)
{
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.Symbol.Trim().ToUpperInvariant()); command.Parameters.AddWithValue(input.Side.ToLowerInvariant());
    command.Parameters.AddWithValue(input.Quantity); command.Parameters.AddWithValue(input.Price);
    command.Parameters.AddWithValue(input.Currency.Trim().ToUpperInvariant()); command.Parameters.AddWithValue(input.TradedAt.ToUniversalTime());
    command.Parameters.AddWithValue(input.Notes ?? ""); command.Parameters.AddWithValue(diaryId);
}

static TransactionResponse ReadTransaction(NpgsqlDataReader reader) => new(
    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5),
    reader.GetString(6).Trim(), reader.GetDateTime(7), reader.GetString(8), reader.GetDateTime(9), reader.GetDateTime(10));

record DiaryWrite(DateOnly LocalDate, string Title, string? Content);
record QuickNote(DateOnly LocalDate, string Content, Guid? TargetDiaryId);
record DiaryResponse(Guid Id, DateOnly LocalDate, string Title, string Content, DateTime CreatedAt, DateTime UpdatedAt);
record TransactionWrite(string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string? Notes);
record TransactionResponse(Guid Id, Guid DiaryId, string Symbol, string Side, decimal Quantity, decimal Price, string Currency, DateTime TradedAt, string Notes, DateTime CreatedAt, DateTime UpdatedAt);

sealed class OutboxPublisher(NpgsqlDataSource db, IHttpClientFactory clients, IConfiguration configuration, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishOnce(stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(error, "Journal outbox publish failed"); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task PublishOnce(CancellationToken cancellationToken)
    {
        await using var command = db.CreateCommand("SELECT event_id,event_type,event_version,payload::text FROM journal.outbox_events WHERE published_at IS NULL ORDER BY occurred_at LIMIT 20");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<(Guid Id, string Type, int Version, string Payload)>();
        while (await reader.ReadAsync(cancellationToken)) events.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        foreach (var item in events)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/events/diary-deleted");
            request.Headers.Add("X-Service-Key", configuration["Internal:ServiceKey"] ?? "local-service-key");
            request.Content = JsonContent.Create(new { eventId = item.Id, eventType = item.Type, version = item.Version, payload = JsonSerializer.Deserialize<JsonElement>(item.Payload) });
            using var response = await clients.CreateClient("reminder").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            await using var mark = db.CreateCommand("UPDATE journal.outbox_events SET published_at=now() WHERE event_id=$1 AND published_at IS NULL");
            mark.Parameters.AddWithValue(item.Id); await mark.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
