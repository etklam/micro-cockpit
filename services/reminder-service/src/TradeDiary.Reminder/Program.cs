using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Reminder") ?? "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddHttpClient("journal", client => client.BaseAddress = new Uri(builder.Configuration["Services:Journal"] ?? "http://127.0.0.1:5101"));
// IN-APP reminder delivery channel: writes the reminder_delivery_attempts row (status='delivered') users read in-app.
// This is the real in-app channel — NOT a stub for email/push. A future email/push channel would be a separate
// IReminderDeliveryChannel implementation registered instead of, or alongside, this one.
builder.Services.AddSingleton<IReminderDeliveryChannel, InAppReminderDeliveryChannel>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddSingleton<IAuthorizationHandler, ServiceKeyAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build();
    options.DefaultPolicy = humanOnly;
    options.FallbackPolicy = humanOnly;
    options.AddPolicy(ReminderAuthorizationPolicies.ServiceKey, policy => policy.AddRequirements(new ServiceKeyRequirement()));
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "reminder-service", version = "0.1.0" })).AllowAnonymous();

app.MapPost("/internal/events/diary-deleted", async (DiaryDeletedV1Envelope? input, NpgsqlDataSource db) =>
{
    if (!DiaryDeletedHandler.IsValid(input))
        return Results.Problem("invalid_event", statusCode: 400);
    await DiaryDeletedHandler.ProcessAsync(db, input!);
    return Results.NoContent();
})
.RequireAuthorization(ReminderAuthorizationPolicies.ServiceKey)
.Produces(204).ProducesProblem(400).ProducesProblem(401);

app.MapGet("/internal/diary-alerts", async (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("""
        SELECT id,diary_id,start_local_date,next_local_date,local_time,timezone,repeat_mode,recurrence_end_local_date,next_trigger_at,status,created_at,updated_at
        FROM reminder.diary_alerts WHERE user_id=$1 ORDER BY created_at DESC
        """);
    command.Parameters.AddWithValue(userId); await using var reader = await command.ExecuteReaderAsync();
    var items = new List<DiaryAlertResponse>(); while (await reader.ReadAsync()) items.Add(Read(reader)); return Results.Ok(new CollectionResponse<DiaryAlertResponse>(items));
})
.Produces<CollectionResponse<DiaryAlertResponse>>(200).ProducesProblem(401);

app.MapPost("/internal/diary-alerts", async (DiaryAlertWrite input, HttpRequest request, NpgsqlDataSource db, IHttpClientFactory clients) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var schedule = BuildSchedule(input); if (schedule.Error is not null) return Results.Problem(schedule.Error, statusCode: 400);
    using var validation = new HttpRequestMessage(HttpMethod.Get, $"/internal/diaries/{input.DiaryId}");
    validation.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.ToString());
    using var response = await clients.CreateClient("journal").SendAsync(validation);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.Problem("not_found", statusCode: 404);
    if (!response.IsSuccessStatusCode) return Results.Problem("journal_unavailable", statusCode: 503);
    var id = Guid.NewGuid();
    await using var command = db.CreateCommand("""
        INSERT INTO reminder.diary_alerts(id,user_id,diary_id,start_local_date,next_local_date,local_time,timezone,repeat_mode,recurrence_end_local_date,next_trigger_at,status)
        VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
        RETURNING id,diary_id,start_local_date,next_local_date,local_time,timezone,repeat_mode,recurrence_end_local_date,next_trigger_at,status,created_at,updated_at
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(input.DiaryId);
    AddScheduleParameters(command, input, schedule);
    await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
    return Results.Created($"/internal/diary-alerts/{id}", Read(reader));
})
.Produces<DiaryAlertResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);

app.MapPut("/internal/diary-alerts/{id:guid}", async (Guid id, DiaryAlertWrite input, HttpRequest request, NpgsqlDataSource db, IHttpClientFactory clients) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var schedule = BuildSchedule(input); if (schedule.Error is not null) return Results.Problem(schedule.Error, statusCode: 400);
    using var validation = new HttpRequestMessage(HttpMethod.Get, $"/internal/diaries/{input.DiaryId}"); validation.Headers.TryAddWithoutValidation("Authorization", request.Headers.Authorization.ToString());
    using var response = await clients.CreateClient("journal").SendAsync(validation);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Results.Problem("not_found", statusCode: 404);
    if (!response.IsSuccessStatusCode) return Results.Problem("journal_unavailable", statusCode: 503);
    await using var command = db.CreateCommand("""
        UPDATE reminder.diary_alerts SET diary_id=$3,start_local_date=$4,next_local_date=$5,local_time=$6,timezone=$7,repeat_mode=$8,
          recurrence_end_local_date=$9,next_trigger_at=$10,status=$11,updated_at=now() WHERE id=$1 AND user_id=$2
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(input.DiaryId); AddScheduleParameters(command, input, schedule);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);

app.MapDelete("/internal/diary-alerts/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("DELETE FROM reminder.diary_alerts WHERE id=$1 AND user_id=$2"); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapPost("/internal/diary-alerts/{id:guid}/dismiss", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("UPDATE reminder.diary_alerts SET status='dismissed',next_trigger_at=NULL,next_local_date=NULL,updated_at=now() WHERE id=$1 AND user_id=$2");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/diary-alerts/day-summary", async (DateOnly date, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT count(*) FROM reminder.diary_alerts WHERE user_id=$1 AND start_local_date <= $2 AND recurrence_end_local_date >= $2 AND status='active'");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(date);
    return Results.Ok(new DaySummaryResponse(date, Convert.ToInt64(await command.ExecuteScalarAsync())));
})
.Produces<DaySummaryResponse>(200).ProducesProblem(401);

app.MapGet("/internal/diary-alerts/day-summaries", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (to < from || to.DayNumber - from.DayNumber > 62) return Results.Problem("invalid_date_range", statusCode: 400);
    await using var command = db.CreateCommand("""
        SELECT day::date,count(*) FROM reminder.diary_alerts a
        CROSS JOIN LATERAL generate_series(greatest(a.start_local_date,$2),least(a.recurrence_end_local_date,$3),'1 day') day
        WHERE a.user_id=$1 AND a.status='active' AND extract(isodow from day) < 6
          AND (a.repeat_mode <> 'none' OR day::date=a.start_local_date)
        GROUP BY day::date ORDER BY day::date
        """);
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to);
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<DayCountResponse>();
    while (await reader.ReadAsync()) items.Add(new DayCountResponse(reader.GetFieldValue<DateOnly>(0), reader.GetInt64(1)));
    return Results.Ok(new CollectionResponse<DayCountResponse>(items));
})
.Produces<CollectionResponse<DayCountResponse>>(200).ProducesProblem(400).ProducesProblem(401);

app.MapPost("/internal/worker/run", async (NpgsqlDataSource db, IReminderDeliveryChannel delivery) => Results.Ok(new WorkerRunResult(await ReminderEngine.RunWorker(db, delivery, 50))))
.RequireAuthorization(ReminderAuthorizationPolicies.ServiceKey)
.Produces<WorkerRunResult>(200);

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) => Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
static Schedule BuildSchedule(DiaryAlertWrite input)
{
    var decision = DiaryAlertSchedule.Create(input.StartLocalDate, input.LocalTime, input.Timezone, input.RepeatMode);
    return new(decision.NextLocalDate, decision.RecurrenceEndLocalDate, decision.NextUtcTrigger, decision.Error);
}
static void AddScheduleParameters(NpgsqlCommand command, DiaryAlertWrite input, Schedule schedule)
{
    command.Parameters.AddWithValue(input.StartLocalDate); command.Parameters.AddWithValue((object?)schedule.NextDate ?? DBNull.Value);
    command.Parameters.AddWithValue(input.LocalTime); command.Parameters.AddWithValue(input.Timezone); command.Parameters.AddWithValue(input.RepeatMode);
    command.Parameters.AddWithValue(schedule.EndDate); command.Parameters.AddWithValue((object?)schedule.NextUtc ?? DBNull.Value);
    command.Parameters.AddWithValue(schedule.NextUtc is null ? "expired" : "active");
}
static DiaryAlertResponse Read(NpgsqlDataReader r) => new(r.GetGuid(0),r.GetGuid(1),r.GetFieldValue<DateOnly>(2),r.IsDBNull(3)?null:r.GetFieldValue<DateOnly>(3),r.GetFieldValue<TimeOnly>(4),r.GetString(5),r.GetString(6),r.GetFieldValue<DateOnly>(7),r.IsDBNull(8)?null:r.GetDateTime(8),r.GetString(9),r.GetDateTime(10),r.GetDateTime(11));

record DiaryAlertWrite(Guid DiaryId, DateOnly StartLocalDate, TimeOnly LocalTime, string Timezone, string RepeatMode);
record Schedule(DateOnly? NextDate, DateOnly EndDate, DateTime? NextUtc, string? Error);
record Due(Guid Id, Guid DiaryId, Guid UserId, DateOnly LocalDate, TimeOnly LocalTime, string Timezone, string RepeatMode, DateOnly EndDate, DateTime ScheduledFor);
record DiaryAlertResponse(Guid Id,Guid DiaryId,DateOnly StartLocalDate,DateOnly? NextLocalDate,TimeOnly LocalTime,string Timezone,string RepeatMode,DateOnly RecurrenceEndLocalDate,DateTime? NextTriggerAt,string Status,DateTime CreatedAt,DateTime UpdatedAt);
record CollectionResponse<T>(List<T> Items);
record DaySummaryResponse(DateOnly Date, long Count);
record DayCountResponse(DateOnly LocalDate, long Count);
record WorkerRunResult(int Delivered);

// Reusable reminder business logic. Lives in a named static class (not a top-level local function) so both the
// /internal/worker/run endpoint and the ReminderWorker hosted service can call it: C# forbids sibling types from
// calling top-level local functions (CS8801).
public static class ReminderEngine
{
    public static DateOnly? NextWeekday(DateOnly candidate, DateOnly end) => DiaryAlertSchedule.NextWeekday(candidate, end);

    public static (DateOnly? LocalDate, DateTime? Utc) CalculateNextOccurrence(DateOnly currentDate, TimeOnly localTime, string timezoneId, string repeatMode, DateOnly endDate)
    {
        var decision = DiaryAlertSchedule.Advance(currentDate, localTime, timezoneId, repeatMode, endDate);
        return (decision.NextLocalDate, decision.NextUtcTrigger);
    }

    // Claims due diary_alerts with FOR UPDATE SKIP LOCKED (multi-instance safe), delivers each reminder through the
    // supplied channel, and advances recurrence. The only change from the old inline RunWorker is that the
    // reminder_delivery_attempts insert is delegated to IReminderDeliveryChannel, executed within the same transaction
    // so delivery and the trigger advance commit (or roll back) atomically.
    public static async Task<int> RunWorker(NpgsqlDataSource db, IReminderDeliveryChannel delivery, int limit)
    {
        await using var connection = await db.OpenConnectionAsync(); await using var tx = await connection.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("""
            SELECT id,diary_id,user_id,next_local_date,local_time,timezone,repeat_mode,recurrence_end_local_date,next_trigger_at
            FROM reminder.diary_alerts WHERE status='active' AND next_trigger_at<=now()
            ORDER BY next_trigger_at FOR UPDATE SKIP LOCKED LIMIT $1
            """, connection, tx); claim.Parameters.AddWithValue(limit);
        var due = new List<Due>(); await using (var reader = await claim.ExecuteReaderAsync()) while (await reader.ReadAsync()) due.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<TimeOnly>(4), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateOnly>(7), reader.GetDateTime(8)));
        foreach (var item in due)
        {
            await delivery.DeliverAsync(new ReminderDeliveryRequest(item.Id, item.DiaryId, item.UserId, item.ScheduledFor), connection, tx);
            var next = CalculateNextOccurrence(item.LocalDate, item.LocalTime, item.Timezone, item.RepeatMode, item.EndDate);
            var nextDate = next.LocalDate;
            var nextUtc = next.Utc;
            await using var update = new NpgsqlCommand("UPDATE reminder.diary_alerts SET next_local_date=$2,next_trigger_at=$3,status=$4,updated_at=now() WHERE id=$1", connection, tx);
            update.Parameters.AddWithValue(item.Id); update.Parameters.AddWithValue((object?)nextDate ?? DBNull.Value); update.Parameters.AddWithValue((object?)nextUtc ?? DBNull.Value); update.Parameters.AddWithValue(nextUtc is null ? "expired" : "active"); await update.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync(); return due.Count;
    }
}

// Delivery abstraction. InAppReminderDeliveryChannel (below) is the production IN-APP channel that writes the
// reminder_delivery_attempts row. This is NOT a stub for email/push — those would be separate implementations
// (e.g. EmailReminderDeliveryChannel) registered in addition to or instead of the in-app one.
public interface IReminderDeliveryChannel
{
    // Delivers within the caller's open transaction so the delivery row and the alert advance commit atomically.
    Task DeliverAsync(ReminderDeliveryRequest request, NpgsqlConnection connection, NpgsqlTransaction transaction);
}

// Carries everything a delivery channel may need: AlertId (diary_alert_id) plus DiaryId/UserId context that future
// channels (email/push contact lookup) require, and ScheduledFor which the in-app row records.
public sealed record ReminderDeliveryRequest(Guid AlertId, Guid DiaryId, Guid UserId, DateTime ScheduledFor);

// IN-APP delivery: writes the same reminder_delivery_attempts row (status='delivered') the previous inline code
// wrote. This is the real in-app delivery path, not a placeholder for email or push notifications.
public sealed class InAppReminderDeliveryChannel : IReminderDeliveryChannel
{
    public async Task DeliverAsync(ReminderDeliveryRequest request, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var attempt = new NpgsqlCommand("INSERT INTO reminder.reminder_delivery_attempts(id,diary_alert_id,scheduled_for,delivered_at,status) VALUES($1,$2,$3,now(),'delivered') ON CONFLICT DO NOTHING", connection, transaction);
        attempt.Parameters.AddWithValue(Guid.NewGuid()); attempt.Parameters.AddWithValue(request.AlertId); attempt.Parameters.AddWithValue(request.ScheduledFor);
        await attempt.ExecuteNonQueryAsync();
    }
}

// Hosted worker: claims due diary_alerts on a schedule and delivers via IReminderDeliveryChannel by reusing
// ReminderEngine.RunWorker (the same logic as POST /internal/worker/run). Single-run failures are logged, not fatal.
sealed class ReminderWorker(NpgsqlDataSource db, IConfiguration configuration, IReminderDeliveryChannel delivery, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Workers:Reminder:Enabled", true);
        if (!enabled) { logger.LogInformation("Reminder worker disabled (Workers:Reminder:Enabled=false); hosted service idle."); return; }
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Workers:Reminder:IntervalSeconds", 30));
        logger.LogInformation("Reminder worker starting; interval {Interval}s.", interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var runId = Guid.NewGuid();
            using (logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId }))
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    logger.LogInformation("Reminder run starting.");
                    var count = await ReminderEngine.RunWorker(db, delivery, 50);
                    logger.LogInformation("Reminder run completed: delivered {Count} in {DurationMs}ms.", count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "Reminder run failed in {DurationMs}ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { }
        }
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
        var scheme = metadata.OfType<IAuthorizeData>().Any(data => data.Policy == ReminderAuthorizationPolicies.ServiceKey)
            ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}

static class ReminderAuthorizationPolicies
{
    public const string ServiceKey = "serviceKey";
}

sealed class ServiceKeyRequirement : IAuthorizationRequirement { }

sealed class ServiceKeyAuthorizationHandler(IConfiguration configuration) : AuthorizationHandler<ServiceKeyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ServiceKeyRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext && ServiceKeyAuthorization.IsValid(
                httpContext.Request.Headers["X-Service-Key"].ToString(),
                configuration["Internal:ServiceKey"] ?? ""))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class ServiceKeyAuthorization
{
    public static bool IsValid(string supplied, string expected)
    {
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
