using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Security.Cryptography;
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
builder.Services.AddHostedService<OutboxPublisher>();
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

diary.MapGet("/diaries", async (HttpRequest request, NpgsqlDataSource db) =>
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
    return Results.Ok(new CollectionResponse<DiaryResponse>(items));
})
.Produces<CollectionResponse<DiaryResponse>>(200).ProducesProblem(401);

diary.MapGet("/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT id, local_date, title, content, created_at, updated_at
        FROM journal.diaries WHERE id = $1 AND user_id = $2 AND deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(ReadDiary(reader)) : Results.Problem("not_found", statusCode: 404);
})
.Produces<DiaryResponse>(200).ProducesProblem(401).ProducesProblem(404);

diary.MapGet("/diary-day-summary", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
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
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.Problem("title_required", statusCode: 400);
    if (!TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await Idempotent(db, userId, "create-diary", key, input, async (connection, tx) =>
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
        return Stored(201, $"/internal/diaries/{id}", new DiaryResponse(id, input.LocalDate, input.Title.Trim(), input.Content ?? "", reader.GetDateTime(0), reader.GetDateTime(1)));
    });
    return WriteResult(request.HttpContext, result);
})
.Produces<DiaryResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapPut("/diaries/{id:guid}", async (Guid id, DiaryWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.Problem("title_required", statusCode: 400);
    await using var command = db.CreateCommand("""
        UPDATE journal.diaries SET local_date=$3, title=$4, content=$5, updated_at=now()
        WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(input.LocalDate);
    command.Parameters.AddWithValue(input.Title.Trim());
    command.Parameters.AddWithValue(input.Content ?? "");
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

diary.MapDelete("/diaries/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
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

diary.MapPost("/quick-note", async (QuickNote input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Content)) return Results.Problem("content_required", statusCode: 400);
    if (!TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await Idempotent(db, userId, "quick-note", key, input, async (connection, tx) =>
    {
        if (input.TargetDiaryId is { } target)
        {
            await using var append = new NpgsqlCommand("""
                UPDATE journal.diaries SET content = concat_ws(E'\n\n', nullif(content, ''), $3), updated_at=now()
                WHERE id=$1 AND user_id=$2 AND deleted_at IS NULL
                """, connection, tx);
            append.Parameters.AddWithValue(target); append.Parameters.AddWithValue(userId); append.Parameters.AddWithValue(input.Content.Trim());
            return await append.ExecuteNonQueryAsync() == 0 ? Stored(404, null, new { error = "not_found" }) : Stored(200, null, new QuickNoteResponse(target, true));
        }

        var id = Guid.NewGuid();
        await using var create = new NpgsqlCommand("INSERT INTO journal.diaries (id,user_id,local_date,title,content) VALUES ($1,$2,$3,'Quick note',$4)", connection, tx);
        create.Parameters.AddWithValue(id); create.Parameters.AddWithValue(userId); create.Parameters.AddWithValue(input.LocalDate); create.Parameters.AddWithValue(input.Content.Trim());
        await create.ExecuteNonQueryAsync();
        return Stored(201, $"/internal/diaries/{id}", new QuickNoteResponse(id, false));
    });
    return WriteResult(request.HttpContext, result);
})
.Produces<QuickNoteResponse>(200).Produces<QuickNoteResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapGet("/diaries/{diaryId:guid}/transactions", async (Guid diaryId, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (!await OwnsDiary(db, diaryId, userId)) return Results.Problem("not_found", statusCode: 404);
    await using var command = db.CreateCommand("""
        SELECT id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
        FROM journal.transactions WHERE diary_id=$1 AND user_id=$2 AND deleted_at IS NULL ORDER BY traded_at DESC
        """);
    command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    await using var reader = await command.ExecuteReaderAsync();
    var items = new List<TransactionResponse>();
    while (await reader.ReadAsync()) items.Add(ReadTransaction(reader));
    return Results.Ok(new CollectionResponse<TransactionResponse>(items));
})
.Produces<CollectionResponse<TransactionResponse>>(200).ProducesProblem(401).ProducesProblem(404);

diary.MapPost("/diaries/{diaryId:guid}/transactions", async (Guid diaryId, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var error = ValidateTransaction(input);
    if (error is not null) return Results.Problem(error, statusCode: 400);
    if (!TryIdempotencyKey(request, out var key)) return Results.Problem("invalid_idempotency_key", statusCode: 400);
    var result = await Idempotent(db, userId, $"create-transaction:{diaryId}", key, input, async (connection, tx) =>
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO journal.transactions (id,diary_id,user_id,symbol,side,quantity,price,currency,traded_at,notes)
            SELECT $1,id,$2,$3,$4,$5,$6,$7,$8,$9 FROM journal.diaries
            WHERE id=$10 AND user_id=$2 AND deleted_at IS NULL
            RETURNING id,diary_id,symbol,side,quantity,price,currency,traded_at,notes,created_at,updated_at
            """, connection, tx);
        AddTransactionParameters(command, id, userId, diaryId, input);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? Stored(201, $"/internal/diaries/{diaryId}/transactions/{id}", ReadTransaction(reader))
            : Stored(404, null, new { error = "not_found" });
    });
    return WriteResult(request.HttpContext, result);
})
.Produces<TransactionResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)
.WithMetadata(new IdempotencyKeyHeaderMarker());

diary.MapPut("/diaries/{diaryId:guid}/transactions/{id:guid}", async (Guid diaryId, Guid id, TransactionWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var error = ValidateTransaction(input);
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
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        UPDATE journal.transactions SET deleted_at=now(),updated_at=now()
        WHERE id=$1 AND diary_id=$2 AND user_id=$3 AND deleted_at IS NULL
          AND EXISTS (SELECT 1 FROM journal.diaries d WHERE d.id=$2 AND d.user_id=$3 AND d.deleted_at IS NULL)
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(diaryId); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) =>
    Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

static bool TryIdempotencyKey(HttpRequest request, out string? key)
{
    key = IdempotencyRules.Normalize(request.Headers["Idempotency-Key"].FirstOrDefault());
    return IdempotencyRules.IsValid(key);
}

static async Task<StoredResult> Idempotent<T>(NpgsqlDataSource db, Guid userId, string operation, string? key, T payload,
    Func<NpgsqlConnection, NpgsqlTransaction, Task<StoredResult>> execute)
{
    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    if (key is null)
    {
        var direct = await execute(connection, tx); await tx.CommitAsync(); return direct;
    }

    var hash = IdempotencyRules.ComputeRequestHash(payload);
    await using var reserve = new NpgsqlCommand("""
        INSERT INTO journal.idempotency_keys(user_id,operation,idempotency_key,request_hash)
        VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING
        """, connection, tx);
    reserve.Parameters.AddWithValue(userId); reserve.Parameters.AddWithValue(operation); reserve.Parameters.AddWithValue(key); reserve.Parameters.AddWithValue(hash);
    var owner = await reserve.ExecuteNonQueryAsync() == 1;
    if (!owner)
    {
        await using var read = new NpgsqlCommand("""
            SELECT request_hash,status_code,location,response FROM journal.idempotency_keys
            WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3 FOR UPDATE
            """, connection, tx);
        read.Parameters.AddWithValue(userId); read.Parameters.AddWithValue(operation); read.Parameters.AddWithValue(key);
        await using var reader = await read.ExecuteReaderAsync(); await reader.ReadAsync();
        if (reader.GetString(0) != hash) return Stored(409, null, new { error = "idempotency_key_reused" });
        return new StoredResult(reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2), JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)));
    }

    var result = await execute(connection, tx);
    await using var save = new NpgsqlCommand("""
        UPDATE journal.idempotency_keys SET status_code=$4,location=$5,response=$6::jsonb
        WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3
        """, connection, tx);
    save.Parameters.AddWithValue(userId); save.Parameters.AddWithValue(operation); save.Parameters.AddWithValue(key);
    save.Parameters.AddWithValue(result.StatusCode); save.Parameters.AddWithValue((object?)result.Location ?? DBNull.Value); save.Parameters.AddWithValue(result.Body.GetRawText());
    await save.ExecuteNonQueryAsync();

    // The response column is jsonb, which canonicalizes object-key order. Read the
    // just-stored value back before returning so the owner and every replay use the
    // exact same serialized shape, including under concurrent requests.
    await using var stored = new NpgsqlCommand("""
        SELECT status_code,location,response::text FROM journal.idempotency_keys
        WHERE user_id=$1 AND operation=$2 AND idempotency_key=$3
        """, connection, tx);
    stored.Parameters.AddWithValue(userId); stored.Parameters.AddWithValue(operation); stored.Parameters.AddWithValue(key);
    StoredResult storedResult;
    await using (var storedReader = await stored.ExecuteReaderAsync())
    {
        await storedReader.ReadAsync();
        storedResult = new StoredResult(
            storedReader.GetInt32(0),
            storedReader.IsDBNull(1) ? null : storedReader.GetString(1),
            JsonSerializer.Deserialize<JsonElement>(storedReader.GetString(2)));
    }
    await tx.CommitAsync();
    return storedResult;
}

// ponytail: camelCase so PascalCase response records serialize to the same camelCase keys as the anonymous projections they replace.
// One options instance per call; cheap enough here — hoist to a static field if idempotent endpoints ever go hot.
static StoredResult Stored(int statusCode, string? location, object body)
{
    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    return new StoredResult(statusCode, location, JsonSerializer.SerializeToElement(body, options));
}

static IResult WriteResult(HttpContext context, StoredResult result)
{
    if (result.Location is not null) context.Response.Headers.Location = result.Location;
    // 409 idempotency mismatch becomes a RFC7807 problem; 200/201/404 replays stay byte-stored as-is.
    if (result.StatusCode == 409) return Results.Problem("idempotency_key_reused", statusCode: 409);
    return Results.Json(result.Body, statusCode: result.StatusCode);
}

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
record StoredResult(int StatusCode, string? Location, JsonElement Body);
record QuickNoteResponse(Guid? DiaryId, bool Appended);
record DiaryDaySummaryItem(DateOnly LocalDate, long DiaryCount, long TransactionCount);
record CollectionResponse<T>(List<T> Items);

// ponytail: WithOpenApi parameter mutations are dropped by .NET 10 doc generation (hence its deprecation),
// so the Idempotency-Key header is surfaced via a marker + operation transformer instead.
sealed record IdempotencyKeyHeaderMarker;

sealed class IdempotencyKeyHeaderTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotencyKeyHeaderMarker>().Any())
            return Task.CompletedTask;
        operation.Parameters ??= new List<IOpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 200 }
        });
        return Task.CompletedTask;
    }
}

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
            if (item.Type != DiaryDeletedV1Envelope.Type || item.Version != DiaryDeletedV1Envelope.EventVersion) continue;
            var payload = JsonSerializer.Deserialize<DiaryDeletedV1>(item.Payload, JsonSerializerOptions.Web)
                ?? throw new JsonException("DiaryDeleted.v1 payload is required.");
            var deleted = DiaryDeletedV1Envelope.Create(item.Id, payload.DiaryId, payload.UserId);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/events/diary-deleted");
            request.Headers.Add("X-Service-Key", configuration["Internal:ServiceKey"] ?? "local-service-key");
            request.Content = JsonContent.Create(deleted, options: JsonSerializerOptions.Web);
            using var response = await clients.CreateClient("reminder").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            await using var mark = db.CreateCommand("UPDATE journal.outbox_events SET published_at=now() WHERE event_id=$1 AND published_at IS NULL");
            mark.Parameters.AddWithValue(item.Id); await mark.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
