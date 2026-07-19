using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeDiary.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Journal") ?? throw new InvalidOperationException("Connection string 'Journal' is required.")));
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
    options.AddPolicy("diaryAccess", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
    {
        if (context.User.FindFirst("account_type")?.Value != "agent") return true;
        var method = (context.Resource as HttpContext)?.Request.Method;
        var requiredScope = method == HttpMethods.Get ? "diary:read" : "diary:write";
        return context.User.FindAll("scope").Any(claim => claim.Value == requiredScope);
    }));
});
builder.Services.AddHttpClient("reminder", client => client.BaseAddress = new Uri(builder.Configuration["Services:Reminder"] ?? "http://127.0.0.1:5104"));
builder.Services.AddHttpClient("partner", client => client.BaseAddress = new Uri(builder.Configuration["Services:Partner"] ?? "http://127.0.0.1:5109"));
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
    options.AddOperationTransformer<IdempotencyKeyHeaderTransformer>();
});
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); }
    catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); }
}).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "journal-service", version = "0.1.0" })).AllowAnonymous();

var diary = app.MapGroup("/internal").RequireAuthorization("diaryAccess");

diary.MapGet("/diaries", async (
    HttpRequest request,
    NpgsqlDataSource db,
    string? query = null,
    DateOnly? from = null,
    DateOnly? to = null,
    string reviewStatus = "all",
    string? symbol = null,
    string? tag = null,
    string? cursor = null,
    int limit = 20) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    var error = DiaryQuery.Validate(query, from, to, reviewStatus, symbol, tag, limit, cursor, out var parsed);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    return Results.Ok(await DiaryQuery.ReadAsync(db, userId, parsed));
})
.Produces<DiaryPage>(200).ProducesProblem(400).ProducesProblem(401);

diary.MapGet("/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT d.id, d.local_date, d.title, d.content, d.created_at, d.updated_at,
               coalesce((
                 SELECT array_agg(t.tag ORDER BY t.tag)
                 FROM journal.diary_tags t
                 WHERE t.diary_id = d.id AND t.user_id = d.user_id
               ), '{}'::text[]) AS tags
        FROM journal.diaries d
        WHERE d.id = $1 AND d.user_id = $2 AND d.deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(JournalAccess.ReadDiary(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<DiaryResponse>(200).ProducesProblem(401).ProducesProblem(404);

diary.MapGet("/diary-day-summary", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (to < from || to.DayNumber - from.DayNumber > 62) return Results.Problem("invalid_date_range", statusCode: 400);
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
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<DiaryDaySummaryItem>();
    while (await reader.ReadAsync()) items.Add(new DiaryDaySummaryItem(reader.GetFieldValue<DateOnly>(0), reader.GetInt64(1), reader.GetInt64(2)));
    return Results.Ok(new CollectionResponse<DiaryDaySummaryItem>(items));
})
.Produces<CollectionResponse<DiaryDaySummaryItem>>(200).ProducesProblem(400).ProducesProblem(401);

diary.MapPost("/diaries", async (DiaryWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.Problem("title_required", statusCode: 400);
    var tagError = DiaryTags.NormalizeAll(input.Tags, out var tags);
    if (tagError is not null) return Results.Problem(tagError, statusCode: 400);
    if (!JournalAccess.TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await JournalAccess.Idempotent(db, userId, "create-diary", key, input, async (connection, tx) =>
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO journal.diaries (id, user_id, local_date, title, content)
            VALUES ($1, $2, $3, $4, $5)
            RETURNING created_at, updated_at
            """, connection, tx);
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(input.LocalDate);
        command.Parameters.AddWithValue(input.Title.Trim()); command.Parameters.AddWithValue(input.Content ?? "");
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        var createdAt = reader.GetDateTime(0); var updatedAt = reader.GetDateTime(1);
        await reader.DisposeAsync();
        await DiaryTags.ReplaceAsync(connection, tx, id, userId, tags);
        return JournalAccess.Stored(201, $"/internal/diaries/{id}", new DiaryResponse(id, input.LocalDate, input.Title.Trim(), input.Content ?? "", createdAt, updatedAt, tags));
    });
    return JournalAccess.WriteResult(request.HttpContext, result);
})
.Produces<DiaryResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapPut("/diaries/{id:guid}", async (Guid id, DiaryWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.Problem("title_required", statusCode: 400);
    var tagError = DiaryTags.NormalizeAll(input.Tags, out var tags);
    if (tagError is not null) return Results.Problem(tagError, statusCode: 400);
    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand("""
        UPDATE journal.diaries SET local_date=$3, title=$4, content=$5, updated_at=now()
        WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
        """, connection, tx);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.LocalDate);
    command.Parameters.AddWithValue(input.Title.Trim());
    command.Parameters.AddWithValue(input.Content ?? "");
    if (await command.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
    await DiaryTags.ReplaceAsync(connection, tx, id, userId, tags);
    await tx.CommitAsync();
    return Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

diary.MapDelete("/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand("UPDATE journal.diaries SET deleted_at=now(),updated_at=now() WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL", connection, tx);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    if (await command.ExecuteNonQueryAsync() == 0) return Results.Problem("not_found", statusCode: 404);
    var deleted = DiaryDeletedV1Envelope.Create(Guid.NewGuid(), id, userId);
    await using var outbox = new NpgsqlCommand("INSERT INTO journal.outbox_events(event_id,event_type,event_version,payload) VALUES($1,$2,$3,$4::jsonb)", connection, tx);
    outbox.Parameters.AddWithValue(deleted.EventId);
    outbox.Parameters.AddWithValue(deleted.EventType);
    outbox.Parameters.AddWithValue(deleted.Version);
    outbox.Parameters.AddWithValue(JsonSerializer.Serialize(deleted.Payload, JsonSerializerOptions.Web));
    await outbox.ExecuteNonQueryAsync(); await tx.CommitAsync();
    return Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

diary.MapGet("/diaries/{diaryId:guid}/review", async (Guid diaryId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT r.diary_id,r.thesis,r.planned_action,r.actual_action,r.emotion,r.discipline_score,
               r.execution_score,r.process_assessment,r.mistake_tags,r.lesson,r.next_action,r.created_at,r.updated_at
        FROM journal.diary_reviews r JOIN journal.diaries d ON d.id=r.diary_id
        WHERE r.diary_id=$1 AND r.user_id=$2 AND d.user_id=$2 AND d.deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(JournalAccess.ReadReview(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<DiaryReviewResponse>(200).ProducesProblem(401).ProducesProblem(404);

diary.MapPut("/diaries/{diaryId:guid}/review", async (Guid diaryId, DiaryReviewWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    var error = DiaryReviewRules.Validate(input);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    await using var command = db.CreateCommand("""
        INSERT INTO journal.diary_reviews
          (diary_id,user_id,thesis,planned_action,actual_action,emotion,discipline_score,execution_score,process_assessment,mistake_tags,lesson,next_action)
        SELECT d.id,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12 FROM journal.diaries d
        WHERE d.id=$1 AND d.user_id=$2 AND d.deleted_at IS NULL
        ON CONFLICT (diary_id) DO UPDATE SET
          thesis=EXCLUDED.thesis,planned_action=EXCLUDED.planned_action,actual_action=EXCLUDED.actual_action,
          emotion=EXCLUDED.emotion,discipline_score=EXCLUDED.discipline_score,execution_score=EXCLUDED.execution_score,
          process_assessment=EXCLUDED.process_assessment,mistake_tags=EXCLUDED.mistake_tags,
          lesson=EXCLUDED.lesson,next_action=EXCLUDED.next_action,updated_at=now()
        WHERE journal.diary_reviews.user_id=EXCLUDED.user_id
        RETURNING diary_id,thesis,planned_action,actual_action,emotion,discipline_score,execution_score,
                  process_assessment,mistake_tags,lesson,next_action,created_at,updated_at
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    JournalAccess.AddNullableText(command, input.Thesis); JournalAccess.AddNullableText(command, input.PlannedAction); JournalAccess.AddNullableText(command, input.ActualAction);
    JournalAccess.AddNullableText(command, input.Emotion); JournalAccess.AddNullableSmallint(command, input.DisciplineScore);
    JournalAccess.AddNullableSmallint(command, input.ExecutionScore); JournalAccess.AddNullableText(command, input.ProcessAssessment);
    command.Parameters.AddWithValue((input.MistakeTags ?? []).ToArray()); JournalAccess.AddNullableText(command, input.Lesson); JournalAccess.AddNullableText(command, input.NextAction);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(JournalAccess.ReadReview(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<DiaryReviewResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

diary.MapDelete("/diaries/{diaryId:guid}/review", async (Guid diaryId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        DELETE FROM journal.diary_reviews r USING journal.diaries d
        WHERE r.diary_id=$1 AND r.user_id=$2 AND d.id=r.diary_id AND d.user_id=$2 AND d.deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

diary.MapGet("/diary-review-summary", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (DiaryReviewRules.InvalidRange(from, to)) return Results.Problem("invalid_date_range", statusCode: 400);
    const string scope = "FROM journal.diary_reviews r JOIN journal.diaries d ON d.id=r.diary_id WHERE r.user_id=$1 AND d.user_id=$1 AND d.deleted_at IS NULL AND d.local_date BETWEEN $2 AND $3";
    await using var totals = db.CreateCommand($"SELECT count(*),avg(r.discipline_score),avg(r.execution_score) {scope}");
    totals.Parameters.AddWithValue(userId); totals.Parameters.AddWithValue(from); totals.Parameters.AddWithValue(to);
    long count; decimal? disciplineAverage; decimal? executionAverage;
    await using (var reader = await totals.ExecuteReaderAsync())
    {
        await reader.ReadAsync(); count = reader.GetInt64(0);
        disciplineAverage = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        executionAverage = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
    }
    var emotions = await JournalAccess.ReadCounts(db, $"SELECT r.emotion,count(*) {scope} AND r.emotion IS NOT NULL GROUP BY r.emotion ORDER BY r.emotion", userId, from, to);
    var assessments = await JournalAccess.ReadCounts(db, $"SELECT r.process_assessment,count(*) {scope} AND r.process_assessment IS NOT NULL GROUP BY r.process_assessment ORDER BY r.process_assessment", userId, from, to);
    await using var tags = db.CreateCommand("""
        SELECT tag,count(*) total FROM journal.diary_reviews r
        JOIN journal.diaries d ON d.id=r.diary_id CROSS JOIN LATERAL unnest(r.mistake_tags) tag
        WHERE r.user_id=$1 AND d.user_id=$1 AND d.deleted_at IS NULL AND d.local_date BETWEEN $2 AND $3
        GROUP BY tag ORDER BY total DESC,tag LIMIT 5
        """);
    tags.Parameters.AddWithValue(userId); tags.Parameters.AddWithValue(from); tags.Parameters.AddWithValue(to);
    var topTags = new List<MistakeTagCountResponse>();
    await using (var reader = await tags.ExecuteReaderAsync()) while (await reader.ReadAsync()) topTags.Add(new(reader.GetString(0), reader.GetInt64(1)));
    return Results.Ok(new DiaryReviewSummaryResponse(count, disciplineAverage, executionAverage, emotions, assessments, topTags));
})
.Produces<DiaryReviewSummaryResponse>(200).ProducesProblem(400).ProducesProblem(401);

diary.MapGet("/diary-review-items", async (HttpRequest request, NpgsqlDataSource db, DateOnly from, DateOnly to, DiaryReviewFilterStatus status = DiaryReviewFilterStatus.all, DiaryReviewAssessmentFilter assessment = DiaryReviewAssessmentFilter.all, string? tag = null, string? cursor = null, int limit = 50) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    var selectedStatus = status.ToString();
    var selectedAssessment = assessment.ToString();
    var error = DiaryReviewItems.Validate(from, to, selectedStatus, selectedAssessment, tag, limit, cursor, out var parsedCursor);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    return Results.Ok(await DiaryReviewItems.ReadAsync(db, userId, from, to, selectedStatus, selectedAssessment, tag, limit, parsedCursor));
})
.Produces<DiaryReviewItemsResponse>(200).ProducesProblem(400).ProducesProblem(401);

diary.MapPost("/quick-note", async (QuickNote input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Content)) return Results.Problem("content_required", statusCode: 400);
    if (!JournalAccess.TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await JournalAccess.Idempotent(db, userId, "quick-note", key, input, async (connection, tx) =>
    {
        if (input.TargetDiaryId is { } target)
        {
            await using var append = new NpgsqlCommand("""
                UPDATE journal.diaries SET content = concat_ws(E'\n\n', nullif(content, ''), $3), updated_at=now()
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
                """, connection, tx);
            append.Parameters.AddWithValue(target); append.Parameters.AddWithValue(userId); append.Parameters.AddWithValue(input.Content.Trim());
            return await append.ExecuteNonQueryAsync() == 0 ? JournalAccess.Stored(404, null, new { error = "not_found" }) : JournalAccess.Stored(200, null, new QuickNoteResponse(target, true));
        }

        var id = Guid.NewGuid();
        await using var create = new NpgsqlCommand("INSERT INTO journal.diaries (id,user_id,local_date,title,content) VALUES ($1,$2,$3,'Quick note',$4)", connection, tx);
        create.Parameters.AddWithValue(id); create.Parameters.AddWithValue(userId); create.Parameters.AddWithValue(input.LocalDate); create.Parameters.AddWithValue(input.Content.Trim());
        await create.ExecuteNonQueryAsync();
        return JournalAccess.Stored(201, $"/internal/diaries/{id}", new QuickNoteResponse(id, false));
    });
    return JournalAccess.WriteResult(request.HttpContext, result);
})
.Produces<QuickNoteResponse>(200).Produces<QuickNoteResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapGet("/diaries/{diaryId:guid}/transactions", async (Guid diaryId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    if (!await JournalAccess.OwnsDiary(db, diaryId, userId)) return Results.Problem("not_found", statusCode: 404);
    await using var command = db.CreateCommand("""
        SELECT id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
        FROM journal.transactions WHERE diary_id=$1 AND user_id=$2 AND deleted_at IS NULL ORDER BY traded_at DESC
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<TransactionResponse>();
    while (await reader.ReadAsync()) items.Add(JournalAccess.ReadTransaction(reader));
    return Results.Ok(new CollectionResponse<TransactionResponse>(items));
})
.Produces<CollectionResponse<TransactionResponse>>(200).ProducesProblem(401).ProducesProblem(404);

diary.MapPost("/diaries/{diaryId:guid}/transactions", async (Guid diaryId, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    var error = JournalAccess.ValidateTransaction(input);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    if (!JournalAccess.TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await JournalAccess.Idempotent(db, userId, $"create-transaction:{diaryId}", key, input, async (connection, tx) =>
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO journal.transactions (id,diary_id,user_id,symbol,side,quantity,price,currency,traded_at,notes)
            SELECT $1,id,$2,$3,$4,$5,$6,$7,$8,$9 FROM journal.diaries
            WHERE id=$10 AND user_id=$2 AND deleted_at IS NULL
            RETURNING id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
            """, connection, tx);
        JournalAccess.AddTransactionParameters(command, id, userId, diaryId, input);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? JournalAccess.Stored(201, $"/internal/diaries/{diaryId}/transactions/{id}", JournalAccess.ReadTransaction(reader))
            : JournalAccess.Stored(404, null, new { error = "not_found" });
    });
    return JournalAccess.WriteResult(request.HttpContext, result);
})
.Produces<TransactionResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapPut("/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    var error = JournalAccess.ValidateTransaction(input);
    if (error is not null) return Results.Problem(error, statusCode: 400);
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
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

diary.MapDelete("/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!JournalAccess.TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        UPDATE journal.transactions SET deleted_at=now(),updated_at=now()
        WHERE id=$1 AND diary_id=$2 AND user_id=$3 AND deleted_at IS NULL
          AND EXISTS (SELECT 1 FROM journal.diaries d WHERE d.id=$2 AND d.user_id=$3 AND d.deleted_at IS NULL)
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

// Partner-shared diary read. Journal owns the data; Partner owns authorization.
// Returns only diary projection fields — never transactions, reviews, or internal metadata.
diary.MapGet("/partner-diaries", async (
    Guid ownerId,
    DateOnly from,
    DateOnly to,
    HttpRequest request,
    NpgsqlDataSource db,
    IHttpClientFactory httpFactory) =>
{
    if (!JournalAccess.TryUser(request, out var viewerId)) return Results.Unauthorized();
    if (to < from || to.DayNumber - from.DayNumber > 366) return Results.Problem("invalid_date_range", statusCode: 400);
    if (ownerId == viewerId) return Results.Problem("not_found", statusCode: 404);

    var allowed = await PartnerShare.IsDiarySharedAsync(httpFactory, request, ownerId);
    if (allowed is null) return Results.Problem("partner_unavailable", statusCode: 503);
    if (allowed is not true) return Results.Problem("not_found", statusCode: 404);

    await using var command = db.CreateCommand("""
        SELECT d.id, d.local_date, d.title, d.content,
               coalesce((
                 SELECT array_agg(t.tag ORDER BY t.tag)
                 FROM journal.diary_tags t
                 WHERE t.diary_id = d.id AND t.user_id = d.user_id
               ), '{}'::text[]) AS tags
        FROM journal.diaries d
        WHERE d.user_id = $1 AND d.deleted_at IS NULL AND d.local_date BETWEEN $2 AND $3
        ORDER BY d.local_date DESC, d.created_at DESC
        """);
    command.Parameters.AddWithValue(ownerId);
    command.Parameters.AddWithValue(from);
    command.Parameters.AddWithValue(to);
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<PartnerDiaryItem>();
    while (await reader.ReadAsync())
    {
        items.Add(new PartnerDiaryItem(
            reader.GetGuid(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<string[]>(4)));
    }
    return Results.Ok(new CollectionResponse<PartnerDiaryItem>(items));
})
.Produces<CollectionResponse<PartnerDiaryItem>>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);

app.Run();

public partial class Program;
