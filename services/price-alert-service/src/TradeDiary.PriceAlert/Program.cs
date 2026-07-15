using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("PriceAlert")??"Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration";o.RequireHttpsMetadata=false;o.Audience="trade-diary-services";});
builder.Services.AddSingleton<IAuthorizationHandler, ServiceKeyAuthorizationHandler>();
builder.Services.AddAuthorization(o=>
{
    var humanOnly=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build();
    o.DefaultPolicy=humanOnly;o.FallbackPolicy=humanOnly;
    o.AddPolicy("serviceKey", policy => policy.AddRequirements(new ServiceKeyRequirement()));
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
builder.Services.AddSingleton<PriceAlertWorkerMetrics>();
builder.Services.AddHostedService<PriceAlertWorker>();
var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new { status="healthy" })).AllowAnonymous();
app.MapGet("/health/ready",async(NpgsqlDataSource db,PriceAlertWorkerMetrics metrics)=>{try{await db.OpenConnectionAsync();return Results.Ok(new { status="ready",workerLastSuccessUtc=metrics.LastSuccessUtc });}catch{return Results.Json(new { status="not_ready" },statusCode:503);}}).AllowAnonymous();
app.MapGet("/version",()=>Results.Ok(new { service="price-alert-service",version="0.1.0",marketContract="v1" })).AllowAnonymous();

app.MapGet("/internal/price-alerts",async(HttpRequest request,NpgsqlDataSource db)=>{if(!UserId(request,out var user))return Results.Unauthorized();await using var cmd=db.CreateCommand("SELECT id,symbol,condition_type,threshold,lookback_days,direction,status,baseline_close,last_evaluated_date,created_at,updated_at FROM price_alert.alerts WHERE user_id=$1 ORDER BY created_at DESC");cmd.Parameters.AddWithValue(user);await using var r=await cmd.ExecuteReaderAsync();var items=new List<PriceAlertResponse>();while(await r.ReadAsync())items.Add(Read(r));return Results.Ok(new CollectionResponse<PriceAlertResponse>(items));})
.Produces<CollectionResponse<PriceAlertResponse>>(200).ProducesProblem(401);

app.MapPost("/internal/price-alerts",async(PriceAlertWrite input,HttpRequest request,NpgsqlDataSource db)=>
{
    if(!UserId(request,out var user))return Results.Unauthorized();var error=Validate(input);if(error is not null)return Results.Problem(error,statusCode:400);
    var symbol=input.Symbol.Trim().ToUpperInvariant();await using var connection=await db.OpenConnectionAsync();
    if(!await PriceAlertEngine.MarketHealthy(connection))return Results.Json(new { error="market_unhealthy" },statusCode:503);
    var latest=await PriceAlertEngine.LatestClose(connection,symbol);if(latest is null)return Results.Problem("symbol_has_no_published_price",statusCode:400);
    var id=Guid.NewGuid();await using var cmd=new NpgsqlCommand("INSERT INTO price_alert.alerts(id,user_id,symbol,condition_type,threshold,lookback_days,direction,status,baseline_close) VALUES($1,$2,$3,$4,$5,$6,$7,'active',$8) RETURNING id,symbol,condition_type,threshold,lookback_days,direction,status,baseline_close,last_evaluated_date,created_at,updated_at",connection);Add(cmd,id,user,symbol,input,input.ConditionType=="percent_change"?latest.Value.Close:null);await using var r=await cmd.ExecuteReaderAsync();await r.ReadAsync();return Results.Created($"/internal/price-alerts/{id}",Read(r));
})
.Produces<PriceAlertResponse>(201).ProducesProblem(400).ProducesProblem(401);

app.MapPut("/internal/price-alerts/{id:guid}",async(Guid id,PriceAlertWrite input,HttpRequest request,NpgsqlDataSource db)=>
{
    if(!UserId(request,out var user))return Results.Unauthorized();var error=Validate(input);if(error is not null)return Results.Problem(error,statusCode:400);var symbol=input.Symbol.Trim().ToUpperInvariant();await using var connection=await db.OpenConnectionAsync();if(!await PriceAlertEngine.MarketHealthy(connection))return Results.Json(new { error="market_unhealthy" },statusCode:503);var latest=await PriceAlertEngine.LatestClose(connection,symbol);if(latest is null)return Results.Problem("symbol_has_no_published_price",statusCode:400);
    await using var cmd=new NpgsqlCommand("UPDATE price_alert.alerts SET symbol=$3,condition_type=$4,threshold=$5,lookback_days=$6,direction=$7,status='active',baseline_close=$8,last_evaluated_date=NULL,updated_at=now() WHERE id=$1 AND user_id=$2",connection);Add(cmd,id,user,symbol,input,input.ConditionType=="percent_change"?latest.Value.Close:null);return await cmd.ExecuteNonQueryAsync()==0?Results.Problem("not_found",statusCode:404):Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404);

app.MapDelete("/internal/price-alerts/{id:guid}",async(Guid id,HttpRequest request,NpgsqlDataSource db)=>{if(!UserId(request,out var user))return Results.Unauthorized();await using var cmd=db.CreateCommand("DELETE FROM price_alert.alerts WHERE id=$1 AND user_id=$2");cmd.Parameters.AddWithValue(id);cmd.Parameters.AddWithValue(user);return await cmd.ExecuteNonQueryAsync()==0?Results.Problem("not_found",statusCode:404):Results.NoContent();})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapPost("/internal/price-alerts/{id:guid}/dismiss",async(Guid id,HttpRequest request,NpgsqlDataSource db)=>await SetStatus(id,"dismissed",request,db,false))
.Produces(204).ProducesProblem(401).ProducesProblem(404);
app.MapPost("/internal/price-alerts/{id:guid}/reactivate",async(Guid id,HttpRequest request,NpgsqlDataSource db)=>await SetStatus(id,"active",request,db,true))
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/price-alerts/{id:guid}/triggers",async(Guid id,HttpRequest request,NpgsqlDataSource db)=>{if(!UserId(request,out var user))return Results.Unauthorized();await using var exists=db.CreateCommand("SELECT 1 FROM price_alert.alerts WHERE id=$1 AND user_id=$2");exists.Parameters.AddWithValue(id);exists.Parameters.AddWithValue(user);if(await exists.ExecuteScalarAsync() is null)return Results.Problem("not_found",statusCode:404);await using var cmd=db.CreateCommand("SELECT id,trading_date,observed_close,triggered_at,dismissed_at FROM price_alert.triggers WHERE alert_id=$1 ORDER BY triggered_at DESC");cmd.Parameters.AddWithValue(id);await using var r=await cmd.ExecuteReaderAsync();var items=new List<TriggerResponse>();while(await r.ReadAsync())items.Add(new TriggerResponse(r.GetGuid(0),r.GetFieldValue<DateOnly>(1),r.GetDecimal(2),r.GetDateTime(3),r.IsDBNull(4)?null:(DateTime?)r.GetDateTime(4)));return Results.Ok(new CollectionResponse<TriggerResponse>(items));})
.Produces<CollectionResponse<TriggerResponse>>(200).ProducesProblem(401).ProducesProblem(404);

app.MapPost("/internal/worker/run",async(NpgsqlDataSource db)=>Results.Ok(new WorkerRunResponse(await PriceAlertEngine.Evaluate(db,100))))
.RequireAuthorization("serviceKey")
.Produces<WorkerRunResponse>(200);
app.Run();

static bool UserId(HttpRequest r,out Guid id)=>Guid.TryParse(r.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,out id);
static string? Validate(PriceAlertWrite x){if(string.IsNullOrWhiteSpace(x.Symbol)||x.Symbol.Trim().Length>24)return "invalid_symbol";if(x.ConditionType is not ("above" or "below" or "percent_change" or "ma_crossing"))return "invalid_condition_type";if(x.ConditionType is "above" or "below" && x.Threshold<0)return "invalid_threshold";if(x.ConditionType=="percent_change"&&x.Threshold==0)return "invalid_threshold";if(x.ConditionType=="ma_crossing"&&(x.LookbackDays is <2 or >250||x.Direction is not ("above" or "below")))return "invalid_moving_average";return null;}
static void Add(NpgsqlCommand c,Guid id,Guid user,string symbol,PriceAlertWrite x,decimal? baseline){c.Parameters.AddWithValue(id);c.Parameters.AddWithValue(user);c.Parameters.AddWithValue(symbol);c.Parameters.AddWithValue(x.ConditionType);c.Parameters.AddWithValue(x.Threshold);c.Parameters.AddWithValue((object?)x.LookbackDays??DBNull.Value);c.Parameters.AddWithValue((object?)x.Direction??DBNull.Value);c.Parameters.AddWithValue((object?)baseline??DBNull.Value);}
static PriceAlertResponse Read(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetDecimal(3),r.IsDBNull(4)?null:r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetDecimal(7),r.IsDBNull(8)?null:r.GetFieldValue<DateOnly>(8),r.GetDateTime(9),r.GetDateTime(10));
static async Task<IResult> SetStatus(Guid id,string status,HttpRequest request,NpgsqlDataSource db,bool requireHealthy){if(!UserId(request,out var user))return Results.Unauthorized();await using var connection=await db.OpenConnectionAsync();if(requireHealthy&&!await PriceAlertEngine.MarketHealthy(connection))return Results.Json(new { error="market_unhealthy" },statusCode:503);await using var cmd=new NpgsqlCommand("UPDATE price_alert.alerts SET status=$3,last_evaluated_date=CASE WHEN $3='active' THEN NULL ELSE last_evaluated_date END,updated_at=now() WHERE id=$1 AND user_id=$2",connection);cmd.Parameters.AddWithValue(id);cmd.Parameters.AddWithValue(user);cmd.Parameters.AddWithValue(status);return await cmd.ExecuteNonQueryAsync()==0?Results.Problem("not_found",statusCode:404):Results.NoContent();}
record PriceAlertWrite(string Symbol,string ConditionType,decimal Threshold,int? LookbackDays,string? Direction);
record PriceAlertResponse(Guid Id,string Symbol,string ConditionType,decimal Threshold,int? LookbackDays,string? Direction,string Status,decimal? BaselineClose,DateOnly? LastEvaluatedDate,DateTime CreatedAt,DateTime UpdatedAt);
record TriggerResponse(Guid Id,DateOnly TradingDate,decimal ObservedClose,DateTime TriggeredAt,DateTime? DismissedAt);
record WorkerRunResponse(int Triggered);
record Due(Guid Id,string Symbol,string Type,decimal Threshold,int? Lookback,string? Direction,decimal? Baseline);
public record Bar(DateOnly Date,decimal Close);
record CollectionResponse<T>(List<T> Items);

public sealed class PriceAlertWorkerMetrics
{
    public DateTime? LastSuccessUtc { get; private set; }
    public void MarkSuccess() => LastSuccessUtc = DateTime.UtcNow;
}

sealed class PriceAlertWorker(NpgsqlDataSource db, IConfiguration configuration, PriceAlertWorkerMetrics metrics, ILogger<PriceAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Workers:PriceAlert:Enabled", true))
        {
            logger.LogInformation("Price alert worker disabled (Workers:PriceAlert:Enabled=false).");
            return;
        }

        var seconds = Math.Max(1, configuration.GetValue("Workers:PriceAlert:IntervalSeconds", 60));
        var interval = TimeSpan.FromSeconds(seconds);
        logger.LogInformation("Price alert worker starting; interval {IntervalSeconds}s.", seconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var runId = Guid.NewGuid();
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId });
            var started = Stopwatch.GetTimestamp();
            try
            {
                var triggered = await PriceAlertEngine.Evaluate(db, 100);
                metrics.MarkSuccess();
                logger.LogInformation("Price alert run completed; triggered {Triggered}; duration {DurationMs}ms.", triggered, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "Price alert run failed; duration {DurationMs}ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }
}

public static class PriceAlertEngine
{
    public static async Task<bool> MarketHealthy(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT coalesce(bool_and(healthy),false) AND count(*)>0 FROM market.published_provider_health_v1", connection);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    public static async Task<(DateOnly Date, decimal Close)?> LatestClose(NpgsqlConnection connection, string symbol)
    {
        await using var command = new NpgsqlCommand("SELECT trade_date,adjusted_close FROM market_data_public.adjusted_daily_bars_v1 WHERE symbol=$1 ORDER BY trade_date DESC LIMIT 1", connection);
        command.Parameters.AddWithValue(symbol);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetFieldValue<DateOnly>(0), reader.GetDecimal(1)) : null;
    }

    public static async Task<int> Evaluate(NpgsqlDataSource db, int limit)
    {
        await using var connection = await db.OpenConnectionAsync();
        if (!await MarketHealthy(connection)) return 0;
        await using var transaction = await connection.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("SELECT id,symbol,condition_type,threshold,lookback_days,direction,baseline_close FROM price_alert.alerts WHERE status='active' ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT $1", connection, transaction);
        claim.Parameters.AddWithValue(limit);
        var alerts = new List<Due>();
        await using (var reader = await claim.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) alerts.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
        }

        var triggered = 0;
        foreach (var alert in alerts)
        {
            var bars = await ReadBars(connection, transaction, alert.Symbol, alert.Lookback ?? 2);
            if (bars.Count == 0) continue;
            var latest = bars[0];
            var hit = alert.Type switch
            {
                "above" => latest.Close >= alert.Threshold,
                "below" => latest.Close <= alert.Threshold,
                "percent_change" => alert.Baseline is not null && (latest.Close - alert.Baseline.Value) / alert.Baseline.Value * 100m * (alert.Threshold < 0 ? -1 : 1) >= Math.Abs(alert.Threshold),
                "ma_crossing" => Crossed(bars, alert.Lookback!.Value, alert.Direction!),
                _ => false
            };

            await using var update = new NpgsqlCommand("UPDATE price_alert.alerts SET last_evaluated_date=$2,updated_at=now() WHERE id=$1", connection, transaction);
            update.Parameters.AddWithValue(alert.Id); update.Parameters.AddWithValue(latest.Date); await update.ExecuteNonQueryAsync();
            if (!hit) continue;

            await using var insert = new NpgsqlCommand("INSERT INTO price_alert.triggers(id,alert_id,trading_date,observed_close) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING", connection, transaction);
            insert.Parameters.AddWithValue(Guid.NewGuid()); insert.Parameters.AddWithValue(alert.Id); insert.Parameters.AddWithValue(latest.Date); insert.Parameters.AddWithValue(latest.Close);
            if (await insert.ExecuteNonQueryAsync() == 0) continue;
            await using var state = new NpgsqlCommand("UPDATE price_alert.alerts SET status='triggered',updated_at=now() WHERE id=$1", connection, transaction);
            state.Parameters.AddWithValue(alert.Id); await state.ExecuteNonQueryAsync(); triggered++;
        }

        await transaction.CommitAsync();
        return triggered;
    }

    public static bool Crossed(IReadOnlyList<Bar> bars, int lookback, string direction)
    {
        if (bars.Count < lookback + 1) return false;
        var current = bars.Take(lookback).Average(x => x.Close);
        var previous = bars.Skip(1).Take(lookback).Average(x => x.Close);
        return direction == "above" ? bars[0].Close >= current && bars[1].Close < previous : bars[0].Close <= current && bars[1].Close > previous;
    }

    private static async Task<List<Bar>> ReadBars(NpgsqlConnection connection, NpgsqlTransaction transaction, string symbol, int lookback)
    {
        await using var command = new NpgsqlCommand("SELECT trade_date,adjusted_close FROM market_data_public.adjusted_daily_bars_v1 WHERE symbol=$1 ORDER BY trade_date DESC LIMIT $2", connection, transaction);
        command.Parameters.AddWithValue(symbol); command.Parameters.AddWithValue(lookback + 1);
        await using var reader = await command.ExecuteReaderAsync();
        var bars = new List<Bar>(); while (await reader.ReadAsync()) bars.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetDecimal(1))); return bars;
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

sealed class ServiceKeyRequirement : IAuthorizationRequirement { }
sealed class ServiceKeyAuthorizationHandler(IConfiguration configuration) : AuthorizationHandler<ServiceKeyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ServiceKeyRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && ServiceKeyAuthorization.IsValid(httpContext.Request.Headers["X-Service-Key"].FirstOrDefault(), configuration["Internal:ServiceKey"] ?? ""))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
public static class ServiceKeyAuthorization
{
    public static bool IsValid(string? supplied, string expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected)) return false;
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
