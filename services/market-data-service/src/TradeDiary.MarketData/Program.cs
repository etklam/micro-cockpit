using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("MarketData") ?? throw new InvalidOperationException("Connection string 'MarketData' is required.")));
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
var app = builder.Build();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status="ready" }); } catch { return Results.Json(new { status="not_ready" }, statusCode:503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service="market-data-service", version="0.1.0", contract="v1" })).AllowAnonymous();

app.MapPut("/internal/admin/symbols/{raw}", async (string raw, SymbolWrite input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request, config)) return Results.Problem("invalid_service_key", statusCode:403);
    var symbol=raw.Trim().ToUpperInvariant();
    if (symbol.Length is < 1 or > 24 || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Exchange) || input.Currency.Trim().Length != 3) return Results.Problem("invalid_symbol", statusCode:400);
    try { TimeZoneInfo.FindSystemTimeZoneById(input.Timezone); } catch { return Results.Problem("invalid_timezone", statusCode:400); }

    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    var instrumentId = input.InstrumentId;
    if (instrumentId is null)
    {
        await using var existing = new NpgsqlCommand("SELECT instrument_id FROM market.symbols WHERE symbol=$1", connection, tx);
        existing.Parameters.AddWithValue(symbol);
        instrumentId = await existing.ExecuteScalarAsync() as Guid?;
        if (instrumentId is null)
        {
            instrumentId = Guid.NewGuid();
            await using var create = new NpgsqlCommand("INSERT INTO market.instruments(id) VALUES($1)", connection, tx);
            create.Parameters.AddWithValue(instrumentId.Value);
            await create.ExecuteNonQueryAsync();
        }
    }
    else
    {
        await using var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM market.instruments WHERE id=$1)", connection, tx);
        exists.Parameters.AddWithValue(instrumentId.Value);
        if (await exists.ExecuteScalarAsync() is not true) return Results.Problem("unknown_instrument", statusCode:400);
    }

    await using (var retire = new NpgsqlCommand("UPDATE market.symbols SET active=false,updated_at=now() WHERE $3 AND instrument_id=$1 AND symbol<>$2 AND active", connection, tx))
    {
        retire.Parameters.AddWithValue(instrumentId.Value); retire.Parameters.AddWithValue(symbol); retire.Parameters.AddWithValue(input.Active); await retire.ExecuteNonQueryAsync();
    }
    await using (var close = new NpgsqlCommand("UPDATE market.instrument_symbol_history SET valid_to=now() WHERE $3 AND instrument_id=$1 AND symbol<>$2 AND valid_to IS NULL", connection, tx))
    {
        close.Parameters.AddWithValue(instrumentId.Value); close.Parameters.AddWithValue(symbol); close.Parameters.AddWithValue(input.Active); await close.ExecuteNonQueryAsync();
    }
    await using (var cmd = new NpgsqlCommand("""
        INSERT INTO market.symbols(symbol,name,exchange,currency,timezone,active,instrument_id)
        VALUES($1,$2,$3,$4,$5,$6,$7)
        ON CONFLICT(symbol) DO UPDATE SET name=$2,exchange=$3,currency=$4,timezone=$5,active=$6,updated_at=now()
        WHERE market.symbols.instrument_id=$7
        """, connection, tx))
    {
        cmd.Parameters.AddWithValue(symbol); cmd.Parameters.AddWithValue(input.Name.Trim()); cmd.Parameters.AddWithValue(input.Exchange.Trim());
        cmd.Parameters.AddWithValue(input.Currency.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue(input.Timezone); cmd.Parameters.AddWithValue(input.Active); cmd.Parameters.AddWithValue(instrumentId.Value);
        if (await cmd.ExecuteNonQueryAsync() == 0) return Results.Problem("symbol_belongs_to_another_instrument", statusCode:409);
    }
    await using (var history = new NpgsqlCommand("""
        INSERT INTO market.instrument_symbol_history(instrument_id,symbol,valid_from)
        SELECT $1,$2,now()
        WHERE $3 AND NOT EXISTS (SELECT 1 FROM market.instrument_symbol_history WHERE instrument_id=$1 AND symbol=$2 AND valid_to IS NULL)
        """, connection, tx))
    {
        history.Parameters.AddWithValue(instrumentId.Value); history.Parameters.AddWithValue(symbol); history.Parameters.AddWithValue(input.Active); await history.ExecuteNonQueryAsync();
    }
    if (!input.Active)
    {
        await using var close = new NpgsqlCommand("UPDATE market.instrument_symbol_history SET valid_to=now() WHERE instrument_id=$1 AND symbol=$2 AND valid_to IS NULL", connection, tx);
        close.Parameters.AddWithValue(instrumentId.Value); close.Parameters.AddWithValue(symbol); await close.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();
    return Results.NoContent();
})
 .Produces(204).ProducesProblem(400).ProducesProblem(403).WithMetadata(new ServiceKeyMetadata());

app.MapPost("/internal/admin/provider-runs", async (ProviderRunWrite input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Problem("invalid_service_key", statusCode:403); if (string.IsNullOrWhiteSpace(input.Provider)) return Results.Problem("invalid_provider", statusCode:400);
    var id=Guid.NewGuid(); await using var cmd=db.CreateCommand("INSERT INTO market.provider_runs(id,provider,started_at,status) VALUES($1,$2,now(),'running')"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(input.Provider.Trim()); await cmd.ExecuteNonQueryAsync(); return Results.Created($"/internal/admin/provider-runs/{id}",new ProviderRunCreatedResponse(id));
})
.Produces<ProviderRunCreatedResponse>(201).ProducesProblem(400).ProducesProblem(403).WithMetadata(new ServiceKeyMetadata());

app.MapPut("/internal/admin/provider-runs/{id:guid}/bars", async (Guid id, List<BarWrite> bars, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Problem("invalid_service_key", statusCode:403); if (bars.Count is 0 or > 5000 || bars.Any(x => !ValidBar(x))) return Results.Problem("invalid_bars", statusCode:400);
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using var owner=new NpgsqlCommand("SELECT provider FROM market.provider_runs WHERE id=$1 AND status='running' FOR UPDATE",connection,tx); owner.Parameters.AddWithValue(id); var provider=(string?)await owner.ExecuteScalarAsync(); if (provider is null) return Results.Problem("not_found", statusCode:404);
    foreach(var bar in bars) { var symbol=bar.Symbol.Trim().ToUpperInvariant(); await using var cmd=new NpgsqlCommand("INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,adjusted_close,volume,provider,provider_run_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) ON CONFLICT(symbol,trading_date,provider_run_id) DO UPDATE SET open=$3,high=$4,low=$5,close=$6,adjusted_close=$7,volume=$8,provider=$9,ingested_at=now(),published_at=NULL",connection,tx); cmd.Parameters.AddWithValue(symbol); cmd.Parameters.AddWithValue(bar.TradingDate); cmd.Parameters.AddWithValue(bar.Open); cmd.Parameters.AddWithValue(bar.High); cmd.Parameters.AddWithValue(bar.Low); cmd.Parameters.AddWithValue(bar.Close); cmd.Parameters.AddWithValue(bar.AdjustedClose??bar.Close); cmd.Parameters.AddWithValue(bar.Volume); cmd.Parameters.AddWithValue(provider); cmd.Parameters.AddWithValue(id); try { await cmd.ExecuteNonQueryAsync(); } catch(PostgresException e) when(e.SqlState=="23503") { return Results.Problem("unknown_symbol", statusCode:400); } }
    await using var count=new NpgsqlCommand("UPDATE market.provider_runs SET rows_received=rows_received+$2 WHERE id=$1",connection,tx); count.Parameters.AddWithValue(id); count.Parameters.AddWithValue(bars.Count); await count.ExecuteNonQueryAsync(); await tx.CommitAsync(); return Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).WithMetadata(new ServiceKeyMetadata());

app.MapPost("/internal/admin/provider-runs/{id:guid}/complete", async (Guid id, CompleteRun input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Problem("invalid_service_key", statusCode:403); if (input.Status is not ("succeeded" or "failed")) return Results.Problem("invalid_status", statusCode:400);
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using var done=new NpgsqlCommand("UPDATE market.provider_runs SET status=$2,error=$3,completed_at=now() WHERE id=$1 AND status='running'",connection,tx); done.Parameters.AddWithValue(id); done.Parameters.AddWithValue(input.Status); done.Parameters.AddWithValue((object?)input.Error??DBNull.Value); if(await done.ExecuteNonQueryAsync()==0) return Results.Problem("not_found", statusCode:404);
    if(input.Status=="succeeded") { await using var publish=new NpgsqlCommand("UPDATE market.daily_bars SET published_at=now() WHERE provider_run_id=$1",connection,tx); publish.Parameters.AddWithValue(id); await publish.ExecuteNonQueryAsync(); }
    await tx.CommitAsync(); return Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).WithMetadata(new ServiceKeyMetadata());

app.MapGet("/internal/v1/symbols", async (NpgsqlDataSource db) => { await using var cmd=db.CreateCommand("SELECT instrument_id,symbol,name,exchange,currency,timezone FROM market.published_symbols_v1 ORDER BY symbol"); await using var r=await cmd.ExecuteReaderAsync(); var items=new List<PublishedSymbolResponse>(); while(await r.ReadAsync()) items.Add(new PublishedSymbolResponse(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5))); return Results.Ok(new SymbolsResponse(1, items)); })
.AllowAnonymous().Produces<SymbolsResponse>(200);

app.MapGet("/internal/v1/instruments/{id:guid}", async (Guid id, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("SELECT instrument_id,symbol,name,exchange,currency,timezone FROM market.published_symbols_v1 WHERE instrument_id=$1");
    cmd.Parameters.AddWithValue(id);
    await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? Results.Ok(new PublishedSymbolResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)))
        : Results.Problem("not_found", statusCode:404);
})
.AllowAnonymous().Produces<PublishedSymbolResponse>(200).ProducesProblem(404);

app.MapGet("/internal/v1/bars/{raw}", async (string raw, DateOnly? from, DateOnly? to, NpgsqlDataSource db) => { var symbol=raw.Trim().ToUpperInvariant(); var end=to??DateOnly.FromDateTime(DateTime.UtcNow); var start=from??end.AddDays(-365); if(end<start || end.DayNumber-start.DayNumber>3660) return Results.Problem("invalid_date_range", statusCode:400); await using var cmd=db.CreateCommand("SELECT trading_date,open,high,low,raw_close,adjusted_close,volume,provider,published_at FROM market.published_daily_bars_v1 WHERE symbol=$1 AND trading_date BETWEEN $2 AND $3 ORDER BY trading_date"); cmd.Parameters.AddWithValue(symbol);cmd.Parameters.AddWithValue(start);cmd.Parameters.AddWithValue(end);await using var r=await cmd.ExecuteReaderAsync();var items=new List<PublishedBarResponse>();while(await r.ReadAsync())items.Add(new PublishedBarResponse(r.GetFieldValue<DateOnly>(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDecimal(4),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),r.GetString(7),r.GetDateTime(8)));return Results.Ok(new BarsResponse(1, symbol, items)); })
.AllowAnonymous().Produces<BarsResponse>(200).ProducesProblem(400);

app.MapGet("/internal/v1/instruments/{id:guid}/daily-close", async (Guid id, DateOnly? onOrBefore, NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("""
        SELECT s.symbol,b.trading_date,b.raw_close,b.adjusted_close,b.provider,b.published_at
        FROM market.published_symbols_v1 s
        LEFT JOIN LATERAL (
            SELECT trading_date,raw_close,adjusted_close,provider,published_at
            FROM market.published_daily_bars_v1
            WHERE symbol=s.symbol AND trading_date <= $2
            ORDER BY trading_date DESC LIMIT 1
        ) b ON true
        WHERE s.instrument_id=$1
        """);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(onOrBefore ?? DateOnly.FromDateTime(DateTime.UtcNow));
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Problem("not_found", statusCode:404);
    return reader.IsDBNull(1)
        ? Results.Ok(new DailyCloseResponse(id, reader.GetString(0), "unavailable", null, null, null, null, null))
        : Results.Ok(new DailyCloseResponse(
            id, reader.GetString(0), "available", reader.GetFieldValue<DateOnly>(1),
            reader.GetDecimal(2), reader.GetDecimal(3), reader.GetString(4),
            DateTime.SpecifyKind(reader.GetDateTime(5),DateTimeKind.Utc)));
})
.AllowAnonymous().Produces<DailyCloseResponse>(200).ProducesProblem(404);

app.MapGet("/internal/v1/providers/health", async (NpgsqlDataSource db) => { await using var cmd=db.CreateCommand("SELECT provider,last_success_at,healthy FROM market.published_provider_health_v1 ORDER BY provider");await using var r=await cmd.ExecuteReaderAsync();var items=new List<ProviderHealthResponse>();while(await r.ReadAsync())items.Add(new ProviderHealthResponse(r.GetString(0),r.GetDateTime(1),r.GetBoolean(2)));return Results.Ok(new ProvidersHealthResponse(1, items.Count>0 && items.All(x=>x.Healthy), items)); })
.AllowAnonymous().Produces<ProvidersHealthResponse>(200);

app.Run();

static bool Admin(HttpRequest request,IConfiguration config){var a=Encoding.UTF8.GetBytes(request.Headers["X-Service-Key"].ToString());var b=Encoding.UTF8.GetBytes(config["Internal:ServiceKey"]??"");return b.Length>0&&a.Length==b.Length&&CryptographicOperations.FixedTimeEquals(a,b);}
static bool ValidBar(BarWrite x)=>!string.IsNullOrWhiteSpace(x.Symbol)&&x.Open>=0&&x.Low>=0&&x.Close>=0&&(x.AdjustedClose??x.Close)>=0&&x.Volume>=0&&x.High>=Math.Max(x.Open,Math.Max(x.Low,x.Close))&&x.Low<=Math.Min(x.Open,Math.Min(x.High,x.Close));
record SymbolWrite(string Name,string Exchange,string Currency,string Timezone,bool Active=true,Guid? InstrumentId=null);
record ProviderRunWrite(string Provider);
record CompleteRun(string Status,string? Error);
record BarWrite(string Symbol,DateOnly TradingDate,decimal Open,decimal High,decimal Low,decimal Close,decimal Volume,decimal? AdjustedClose=null);
record ProviderRunCreatedResponse(Guid Id);
sealed record ServiceKeyMetadata;
record PublishedSymbolResponse(Guid InstrumentId,string Symbol,string Name,string Exchange,string Currency,string Timezone);
record SymbolsResponse(int ContractVersion,List<PublishedSymbolResponse> Items);
record PublishedBarResponse(DateOnly TradingDate,decimal Open,decimal High,decimal Low,decimal Close,decimal RawClose,decimal AdjustedClose,decimal Volume,string Provider,DateTime PublishedAt);
record BarsResponse(int ContractVersion,string Symbol,List<PublishedBarResponse> Items);
record DailyCloseResponse(Guid InstrumentId,string Symbol,string Status,DateOnly? TradingDate,decimal? RawClose,decimal? AdjustedClose,string? Provider,DateTime? PublishedAt);
record ProviderHealthResponse(string Provider,DateTime LastSuccessAt,bool Healthy);
record ProvidersHealthResponse(int ContractVersion,bool Healthy,List<ProviderHealthResponse> Items);

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
        var scheme = metadata.OfType<ServiceKeyMetadata>().Any() ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}

public partial class Program;
