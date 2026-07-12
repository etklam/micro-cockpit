using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Rotation") ?? "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local development only; production must configure HTTPS metadata.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
builder.Services.AddSingleton<RotationWorkerMetrics>();
builder.Services.AddHostedService<RotationWorker>();
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db, RotationWorkerMetrics metrics) => { try { await using var command=db.CreateCommand("SELECT to_regclass('market_data_public.adjusted_daily_bars_v1') IS NOT NULL"); return (bool)(await command.ExecuteScalarAsync())! ? Results.Ok(new { status = "ready", workerLastSuccessUtc = metrics.LastSuccessUtc }) : Results.Json(new { status = "not_ready" },statusCode:503); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "rotation-service", version = "0.1.0", formulaVersion = RotationEngine.FormulaVersion })).AllowAnonymous();

app.MapGet("/internal/rotation/universes", async (NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("SELECT id,code,name,rank_scope,created_at,updated_at FROM rotation.market_rotation_universes ORDER BY code");
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<UniverseResponse>();
    while (await reader.ReadAsync()) items.Add(new UniverseResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4), reader.GetDateTime(5)));
    return Results.Ok(new CollectionResponse<UniverseResponse>(items));
})
.Produces<CollectionResponse<UniverseResponse>>(200);

app.MapPost("/internal/rotation/universes", async (UniverseWrite input, NpgsqlDataSource db) =>
{
    var code = input.Code.Trim().ToUpperInvariant(); var name = input.Name.Trim(); var scope = input.RankScope?.Trim().ToLowerInvariant() ?? "universe";
    if (code.Length is 0 or > 32 || name.Length is 0 or > 100 || scope is not ("universe" or "sector") || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return Results.Problem("invalid_universe", statusCode:400);
    var id=Guid.NewGuid(); await using var command=db.CreateCommand("INSERT INTO rotation.market_rotation_universes(id,code,name,rank_scope) VALUES($1,$2,$3,$4)");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(scope);
    try { await command.ExecuteNonQueryAsync(); } catch (PostgresException e) when (e.SqlState==PostgresErrorCodes.UniqueViolation) { return Results.Problem("code_exists", statusCode:409); }
    return Results.Created($"/internal/rotation/universes/{id}", new UniverseCreatedResponse(id, code, name, scope));
})
.Produces<UniverseCreatedResponse>(201).ProducesProblem(400).ProducesProblem(409);

app.MapPut("/internal/rotation/universes/{id:guid}", async (Guid id, UniverseWrite input, NpgsqlDataSource db) =>
{
    var code=input.Code.Trim().ToUpperInvariant(); var name=input.Name.Trim(); var scope=input.RankScope?.Trim().ToLowerInvariant() ?? "universe";
    if (code.Length is 0 or > 32 || name.Length is 0 or > 100 || scope is not ("universe" or "sector") || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return Results.Problem("invalid_universe", statusCode:400);
    await using var command=db.CreateCommand("UPDATE rotation.market_rotation_universes SET code=$2,name=$3,rank_scope=$4,updated_at=now() WHERE id=$1"); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(scope);
    try { return await command.ExecuteNonQueryAsync()==0 ? Results.Problem("not_found", statusCode:404) : Results.NoContent(); } catch (PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation) { return Results.Problem("code_exists", statusCode:409); }
})
.Produces(204).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);

app.MapDelete("/internal/rotation/universes/{id:guid}", async (Guid id, NpgsqlDataSource db) =>
{
    await using var command=db.CreateCommand("DELETE FROM rotation.market_rotation_universes WHERE id=$1"); command.Parameters.AddWithValue(id);
    return await command.ExecuteNonQueryAsync()==0 ? Results.Problem("not_found", statusCode:404) : Results.NoContent();
})
.Produces(204).ProducesProblem(404);

app.MapPut("/internal/rotation/universes/{id:guid}/symbols", async (Guid id, UniverseSymbolWrite[] input, NpgsqlDataSource db) =>
{
    if (input.Length is 0 or > 500) return Results.Problem("symbols_required", statusCode:400);
    var symbols=input.Select((x,i) => new { Symbol=x.Symbol.Trim().ToUpperInvariant(), Label=x.Label.Trim(), Sector=string.IsNullOrWhiteSpace(x.Sector)?null:x.Sector.Trim(), Order=x.SortOrder??i }).ToArray();
    if (symbols.Any(x => x.Symbol.Length is 0 or > 20 || x.Label.Length is 0 or > 100 || !x.Symbol.All(c => char.IsAsciiLetterOrDigit(c)||c is '.' or '-')) || symbols.Select(x=>x.Symbol).Distinct().Count()!=symbols.Length) return Results.Problem("invalid_symbols", statusCode:400);
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using (var exists=new NpgsqlCommand("SELECT 1 FROM rotation.market_rotation_universes WHERE id=$1",connection,tx)) { exists.Parameters.AddWithValue(id); if(await exists.ExecuteScalarAsync() is null) return Results.Problem("not_found", statusCode:404); }
    await using (var delete=new NpgsqlCommand("DELETE FROM rotation.market_rotation_universe_symbols WHERE universe_id=$1",connection,tx)) { delete.Parameters.AddWithValue(id); await delete.ExecuteNonQueryAsync(); }
    foreach(var symbol in symbols) { await using var insert=new NpgsqlCommand("INSERT INTO rotation.market_rotation_universe_symbols(universe_id,symbol,label,sector,sort_order) VALUES($1,$2,$3,$4,$5)",connection,tx); insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(symbol.Symbol); insert.Parameters.AddWithValue(symbol.Label); insert.Parameters.AddWithValue((object?)symbol.Sector??DBNull.Value); insert.Parameters.AddWithValue(symbol.Order); await insert.ExecuteNonQueryAsync(); }
    await tx.CommitAsync(); return Results.NoContent();
})
.Produces(204).ProducesProblem(400).ProducesProblem(404);

app.MapPost("/internal/rotation/universes/{id:guid}/calculate", async (Guid id, DateOnly date, NpgsqlDataSource db) =>
{
    try { return Results.Ok(await RotationEngine.Calculate(db, id, date)); }
    catch (KeyNotFoundException) { return Results.Problem("not_found", statusCode: 404); }
    catch { return Results.Json(new { error = "calculation_failed" }, statusCode: 503); }
})
.Produces<CalculateResponse>(200).ProducesProblem(404);

app.MapGet("/internal/rotation/monitor", async (string universe, DateOnly? date, NpgsqlDataSource db) =>
{
    var code=universe.Trim().ToUpperInvariant(); await using var command=db.CreateCommand(Sql.Monitor); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue((object?)date??DBNull.Value);
    await using var reader=await command.ExecuteReaderAsync(); if(!await reader.ReadAsync()) return Results.Problem("not_found", statusCode:404);
    var id=reader.GetGuid(0); var universeCode=reader.GetString(1); var universeName=reader.GetString(2); var snapshotDate=reader.IsDBNull(3)?(DateOnly?)null:reader.GetFieldValue<DateOnly>(3); var state=reader.IsDBNull(4)?null:reader.GetString(4); var stateStatus=reader.IsDBNull(5)?"insufficient_data":reader.GetString(5); var breadth=reader.IsDBNull(6)?(decimal?)null:reader.GetDecimal(6); var benchmarkAbove=reader.IsDBNull(7)?(bool?)null:reader.GetBoolean(7); await reader.CloseAsync();
    await using var readConnection=await db.OpenConnectionAsync();
    var sectors=new List<SectorBreadthResponse>(); await using(var q=new NpgsqlCommand("SELECT sector,member_count,available_count,above_ma20_percent,above_ma50_percent,above_ma200_percent,status FROM rotation.sector_breadth_snapshots WHERE universe_id=$1 AND snapshot_date=$2 ORDER BY above_ma20_percent DESC NULLS LAST,sector",readConnection)) { q.Parameters.AddWithValue(id); q.Parameters.AddWithValue((object?)snapshotDate??DBNull.Value); await using var r=await q.ExecuteReaderAsync(); while(await r.ReadAsync()) sectors.Add(new SectorBreadthResponse(r.GetString(0),r.GetInt32(1),r.GetInt32(2),NullableDecimal(r,3),NullableDecimal(r,4),NullableDecimal(r,5),r.GetString(6))); }
    var etfs=new List<EtfSnapshotResponse>(); await using(var q=new NpgsqlCommand("SELECT s.symbol,u.label,s.sector,s.close,s.return_2w,s.return_1m,s.return_3m,s.rank_2w,s.percentile_2w,s.above_ma20,s.above_ma50,s.above_ma200,s.status FROM rotation.market_rotation_snapshots s JOIN rotation.market_rotation_universe_symbols u ON u.universe_id=s.universe_id AND u.symbol=s.symbol WHERE s.universe_id=$1 AND s.snapshot_date=$2 ORDER BY s.rank_2w NULLS LAST,u.sort_order",readConnection)) { q.Parameters.AddWithValue(id); q.Parameters.AddWithValue((object?)snapshotDate??DBNull.Value); await using var r=await q.ExecuteReaderAsync(); while(await r.ReadAsync()) etfs.Add(new EtfSnapshotResponse(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),NullableDecimal(r,3),NullableDecimal(r,4),NullableDecimal(r,5),NullableDecimal(r,6),r.IsDBNull(7)?(int?)null:r.GetInt32(7),NullableDecimal(r,8),NullableBool(r,9),NullableBool(r,10),NullableBool(r,11),r.GetString(12))); }
    return Results.Ok(new MonitorResponse(new MonitorUniverse(id, universeCode, universeName), snapshotDate, RotationEngine.FormulaVersion, snapshotDate is null?"insufficient_data":stateStatus, new MonitorMarketState(state, breadth, benchmarkAbove, stateStatus), sectors, etfs));
})
.Produces<MonitorResponse>(200).ProducesProblem(404);

app.Run();

static decimal? NullableDecimal(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDecimal(i);
static bool? NullableBool(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetBoolean(i);
record UniverseWrite(string Code,string Name,string? RankScope);
record UniverseSymbolWrite(string Symbol,string Label,string? Sector,int? SortOrder);
record UniverseResponse(Guid Id,string Code,string Name,string RankScope,DateTime CreatedAt,DateTime UpdatedAt);
record UniverseCreatedResponse(Guid Id,string Code,string Name,string RankScope);
public record CalculateResponse(Guid UniverseId,DateOnly SnapshotDate,string Status,string FormulaVersion);
record MonitorUniverse(Guid Id,string Code,string Name);
record MonitorMarketState(string? State,decimal? BreadthPercent,bool? BenchmarkAboveMa200,string Status);
record SectorBreadthResponse(string Sector,int MemberCount,int AvailableCount,decimal? AboveMa20Percent,decimal? AboveMa50Percent,decimal? AboveMa200Percent,string Status);
record EtfSnapshotResponse(string Symbol,string Label,string? Sector,decimal? Close,decimal? Return2w,decimal? Return1m,decimal? Return3m,int? Rank2w,decimal? Percentile2w,bool? AboveMa20,bool? AboveMa50,bool? AboveMa200,string Status);
record MonitorResponse(MonitorUniverse Universe,DateOnly? SnapshotDate,string FormulaVersion,string Status,MonitorMarketState MarketState,List<SectorBreadthResponse> SectorBreadth,List<EtfSnapshotResponse> Etfs);
record CollectionResponse<T>(List<T> Items);

static class Sql
{
public const string Snapshot="""
WITH history AS (
 SELECT s.symbol,s.sector,u.rank_scope,b.trade_date,b.adjusted_close,
   lag(b.adjusted_close,10) OVER(PARTITION BY b.symbol ORDER BY b.trade_date) p10,
   lag(b.adjusted_close,20) OVER(PARTITION BY b.symbol ORDER BY b.trade_date) p20,
   lag(b.adjusted_close,63) OVER(PARTITION BY b.symbol ORDER BY b.trade_date) p63,
   avg(b.adjusted_close) OVER(PARTITION BY b.symbol ORDER BY b.trade_date ROWS BETWEEN 19 PRECEDING AND CURRENT ROW) ma20,
   avg(b.adjusted_close) OVER(PARTITION BY b.symbol ORDER BY b.trade_date ROWS BETWEEN 49 PRECEDING AND CURRENT ROW) ma50,
   avg(b.adjusted_close) OVER(PARTITION BY b.symbol ORDER BY b.trade_date ROWS BETWEEN 199 PRECEDING AND CURRENT ROW) ma200,
   count(*) OVER(PARTITION BY b.symbol ORDER BY b.trade_date ROWS BETWEEN 199 PRECEDING AND CURRENT ROW) samples
 FROM rotation.market_rotation_universe_symbols s JOIN rotation.market_rotation_universes u ON u.id=s.universe_id
 LEFT JOIN market_data_public.adjusted_daily_bars_v1 b ON b.symbol=s.symbol AND b.trade_date <= $2 WHERE s.universe_id=$1
), current_rows AS (
 SELECT *, CASE WHEN p10 IS NULL THEN NULL ELSE (adjusted_close/p10-1)*100 END r10,
   CASE WHEN p20 IS NULL THEN NULL ELSE (adjusted_close/p20-1)*100 END r20,
   CASE WHEN p63 IS NULL THEN NULL ELSE (adjusted_close/p63-1)*100 END r63
 FROM history WHERE trade_date=$2
), ranked AS (
 SELECT *,CASE WHEN r10 IS NULL THEN NULL ELSE rank() OVER(PARTITION BY CASE WHEN rank_scope='sector' THEN coalesce(sector,'Unclassified') ELSE '__universe__' END ORDER BY r10 DESC NULLS LAST) END rank10,
   CASE WHEN r10 IS NULL THEN NULL ELSE percent_rank() OVER(PARTITION BY CASE WHEN rank_scope='sector' THEN coalesce(sector,'Unclassified') ELSE '__universe__' END ORDER BY r10) END pct10 FROM current_rows
)
INSERT INTO rotation.market_rotation_snapshots(universe_id,snapshot_date,symbol,rank_scope,sector,close,return_2w,return_1m,return_3m,above_ma20,above_ma50,above_ma200,rank_2w,percentile_2w,status,formula_version)
SELECT $1,$2,s.symbol,u.rank_scope,s.sector,r.adjusted_close,r.r10,r.r20,r.r63,
 CASE WHEN r.adjusted_close IS NULL OR r.ma20 IS NULL THEN NULL ELSE r.adjusted_close>r.ma20 END,
 CASE WHEN r.adjusted_close IS NULL OR r.ma50 IS NULL OR r.samples<50 THEN NULL ELSE r.adjusted_close>r.ma50 END,
 CASE WHEN r.adjusted_close IS NULL OR r.ma200 IS NULL OR r.samples<200 THEN NULL ELSE r.adjusted_close>r.ma200 END,
 r.rank10,r.pct10,CASE WHEN r.samples>=200 AND r.p10 IS NOT NULL AND r.p20 IS NOT NULL AND r.p63 IS NOT NULL THEN 'ok' ELSE 'insufficient_data' END,$3
FROM rotation.market_rotation_universe_symbols s JOIN rotation.market_rotation_universes u ON u.id=s.universe_id LEFT JOIN ranked r ON r.symbol=s.symbol WHERE s.universe_id=$1
""";
public const string Breadth="""
INSERT INTO rotation.sector_breadth_snapshots(universe_id,snapshot_date,sector,member_count,available_count,above_ma20_percent,above_ma50_percent,above_ma200_percent,status,formula_version)
SELECT $1,$2,coalesce(sector,'Unclassified'),count(*)::int,count(above_ma20)::int,
 CASE WHEN count(above_ma20)=0 THEN NULL ELSE round(100.0*count(*) FILTER(WHERE above_ma20)/count(above_ma20),2) END,
 CASE WHEN count(above_ma50)=0 THEN NULL ELSE round(100.0*count(*) FILTER(WHERE above_ma50)/count(above_ma50),2) END,
 CASE WHEN count(above_ma200)=0 THEN NULL ELSE round(100.0*count(*) FILTER(WHERE above_ma200)/count(above_ma200),2) END,
 CASE WHEN count(above_ma20)=count(*) AND count(*)>0 THEN 'ok' ELSE 'insufficient_data' END,$3
FROM rotation.market_rotation_snapshots WHERE universe_id=$1 AND snapshot_date=$2 GROUP BY coalesce(sector,'Unclassified')
""";
public const string State="""
INSERT INTO rotation.market_state_snapshots(universe_id,snapshot_date,state,breadth_percent,benchmark_symbol,benchmark_above_ma200,status,formula_version)
SELECT $1,$2,CASE WHEN b.above_ma200 IS NULL OR x.breadth IS NULL THEN NULL WHEN b.above_ma200 AND x.breadth>=50 THEN 'risk_on' WHEN NOT b.above_ma200 AND x.breadth<50 THEN 'risk_off' ELSE 'mixed' END,
 x.breadth,b.symbol,b.above_ma200,CASE WHEN b.above_ma200 IS NULL OR x.breadth IS NULL THEN 'insufficient_data' ELSE 'ok' END,$3
FROM LATERAL(SELECT symbol,above_ma200 FROM rotation.market_rotation_snapshots WHERE universe_id=$1 AND snapshot_date=$2 ORDER BY (symbol='SPY') DESC,rank_2w NULLS LAST LIMIT 1)b
CROSS JOIN LATERAL(SELECT round(100.0*count(*) FILTER(WHERE above_ma20)/nullif(count(above_ma20),0),2) breadth FROM rotation.market_rotation_snapshots WHERE universe_id=$1 AND snapshot_date=$2)x
""";
public const string Monitor="""
SELECT u.id,u.code,u.name,d.snapshot_date,m.state,m.status,m.breadth_percent,m.benchmark_above_ma200
FROM rotation.market_rotation_universes u
LEFT JOIN LATERAL(SELECT snapshot_date FROM rotation.market_rotation_snapshots WHERE universe_id=u.id AND ($2::date IS NULL OR snapshot_date<=$2) ORDER BY snapshot_date DESC LIMIT 1)d ON true
LEFT JOIN rotation.market_state_snapshots m ON m.universe_id=u.id AND m.snapshot_date=d.snapshot_date WHERE u.code=$1
""";
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
        var path = "/" + (context.Description.RelativePath ?? string.Empty);
        var scheme = path.Contains("/internal/admin/", StringComparison.Ordinal)
                     || path.Contains("/internal/worker/", StringComparison.Ordinal)
                     || path.Contains("/internal/events/", StringComparison.Ordinal)
            ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>()
        });
        return Task.CompletedTask;
    }
}
