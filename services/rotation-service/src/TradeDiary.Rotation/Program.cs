using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

const string FormulaVersion = "rotation-v1";
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
var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await using var command=db.CreateCommand("SELECT to_regclass('market_data_public.adjusted_daily_bars_v1') IS NOT NULL"); return (bool)(await command.ExecuteScalarAsync())! ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "not_ready" },statusCode:503); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "rotation-service", version = "0.1.0", formulaVersion = FormulaVersion })).AllowAnonymous();

app.MapGet("/internal/rotation/universes", async (NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand("SELECT id,code,name,rank_scope,created_at,updated_at FROM rotation.market_rotation_universes ORDER BY code");
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<object>();
    while (await reader.ReadAsync()) items.Add(new { id=reader.GetGuid(0), code=reader.GetString(1), name=reader.GetString(2), rankScope=reader.GetString(3), createdAt=reader.GetDateTime(4), updatedAt=reader.GetDateTime(5) });
    return Results.Ok(new { items });
});

app.MapPost("/internal/rotation/universes", async (UniverseWrite input, NpgsqlDataSource db) =>
{
    var code = input.Code.Trim().ToUpperInvariant(); var name = input.Name.Trim(); var scope = input.RankScope?.Trim().ToLowerInvariant() ?? "universe";
    if (code.Length is 0 or > 32 || name.Length is 0 or > 100 || scope is not ("universe" or "sector") || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return Results.BadRequest(new { error="invalid_universe" });
    var id=Guid.NewGuid(); await using var command=db.CreateCommand("INSERT INTO rotation.market_rotation_universes(id,code,name,rank_scope) VALUES($1,$2,$3,$4)");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(scope);
    try { await command.ExecuteNonQueryAsync(); } catch (PostgresException e) when (e.SqlState==PostgresErrorCodes.UniqueViolation) { return Results.Conflict(new { error="code_exists" }); }
    return Results.Created($"/internal/rotation/universes/{id}", new { id, code, name, rankScope=scope });
});

app.MapPut("/internal/rotation/universes/{id:guid}", async (Guid id, UniverseWrite input, NpgsqlDataSource db) =>
{
    var code=input.Code.Trim().ToUpperInvariant(); var name=input.Name.Trim(); var scope=input.RankScope?.Trim().ToLowerInvariant() ?? "universe";
    if (code.Length is 0 or > 32 || name.Length is 0 or > 100 || scope is not ("universe" or "sector") || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return Results.BadRequest(new { error="invalid_universe" });
    await using var command=db.CreateCommand("UPDATE rotation.market_rotation_universes SET code=$2,name=$3,rank_scope=$4,updated_at=now() WHERE id=$1"); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue(name); command.Parameters.AddWithValue(scope);
    try { return await command.ExecuteNonQueryAsync()==0 ? Results.NotFound() : Results.NoContent(); } catch (PostgresException e) when(e.SqlState==PostgresErrorCodes.UniqueViolation) { return Results.Conflict(new { error="code_exists" }); }
});

app.MapDelete("/internal/rotation/universes/{id:guid}", async (Guid id, NpgsqlDataSource db) =>
{
    await using var command=db.CreateCommand("DELETE FROM rotation.market_rotation_universes WHERE id=$1"); command.Parameters.AddWithValue(id);
    return await command.ExecuteNonQueryAsync()==0 ? Results.NotFound() : Results.NoContent();
});

app.MapPut("/internal/rotation/universes/{id:guid}/symbols", async (Guid id, SymbolWrite[] input, NpgsqlDataSource db) =>
{
    if (input.Length is 0 or > 500) return Results.BadRequest(new { error="symbols_required" });
    var symbols=input.Select((x,i) => new { Symbol=x.Symbol.Trim().ToUpperInvariant(), Label=x.Label.Trim(), Sector=string.IsNullOrWhiteSpace(x.Sector)?null:x.Sector.Trim(), Order=x.SortOrder??i }).ToArray();
    if (symbols.Any(x => x.Symbol.Length is 0 or > 20 || x.Label.Length is 0 or > 100 || !x.Symbol.All(c => char.IsAsciiLetterOrDigit(c)||c is '.' or '-')) || symbols.Select(x=>x.Symbol).Distinct().Count()!=symbols.Length) return Results.BadRequest(new { error="invalid_symbols" });
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using (var exists=new NpgsqlCommand("SELECT 1 FROM rotation.market_rotation_universes WHERE id=$1",connection,tx)) { exists.Parameters.AddWithValue(id); if(await exists.ExecuteScalarAsync() is null) return Results.NotFound(); }
    await using (var delete=new NpgsqlCommand("DELETE FROM rotation.market_rotation_universe_symbols WHERE universe_id=$1",connection,tx)) { delete.Parameters.AddWithValue(id); await delete.ExecuteNonQueryAsync(); }
    foreach(var symbol in symbols) { await using var insert=new NpgsqlCommand("INSERT INTO rotation.market_rotation_universe_symbols(universe_id,symbol,label,sector,sort_order) VALUES($1,$2,$3,$4,$5)",connection,tx); insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(symbol.Symbol); insert.Parameters.AddWithValue(symbol.Label); insert.Parameters.AddWithValue((object?)symbol.Sector??DBNull.Value); insert.Parameters.AddWithValue(symbol.Order); await insert.ExecuteNonQueryAsync(); }
    await tx.CommitAsync(); return Results.NoContent();
});

app.MapPost("/internal/rotation/universes/{id:guid}/calculate", async (Guid id, DateOnly date, NpgsqlDataSource db) =>
{
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync(); var runId=Guid.NewGuid();
    await using (var run=new NpgsqlCommand("""
      INSERT INTO rotation.batch_runs(id,universe_id,snapshot_date,formula_version,status)
      SELECT $1,$2,$3,$4,'running' WHERE EXISTS(SELECT 1 FROM rotation.market_rotation_universes WHERE id=$2)
      ON CONFLICT(universe_id,snapshot_date,formula_version) DO UPDATE SET status='running',started_at=now(),finished_at=NULL,error=NULL RETURNING id
      """,connection,tx)) { run.Parameters.AddWithValue(runId); run.Parameters.AddWithValue(id); run.Parameters.AddWithValue(date); run.Parameters.AddWithValue(FormulaVersion); if(await run.ExecuteScalarAsync() is null) return Results.NotFound(); }
    try
    {
        foreach(var table in new[]{"market_rotation_snapshots","sector_breadth_snapshots","market_state_snapshots"}) { await using var delete=new NpgsqlCommand($"DELETE FROM rotation.{table} WHERE universe_id=$1 AND snapshot_date=$2",connection,tx); delete.Parameters.AddWithValue(id); delete.Parameters.AddWithValue(date); await delete.ExecuteNonQueryAsync(); }
        await using (var snapshots=new NpgsqlCommand(Sql.Snapshot,connection,tx)) { snapshots.Parameters.AddWithValue(id); snapshots.Parameters.AddWithValue(date); snapshots.Parameters.AddWithValue(FormulaVersion); await snapshots.ExecuteNonQueryAsync(); }
        await using (var breadth=new NpgsqlCommand(Sql.Breadth,connection,tx)) { breadth.Parameters.AddWithValue(id); breadth.Parameters.AddWithValue(date); breadth.Parameters.AddWithValue(FormulaVersion); await breadth.ExecuteNonQueryAsync(); }
        await using (var state=new NpgsqlCommand(Sql.State,connection,tx)) { state.Parameters.AddWithValue(id); state.Parameters.AddWithValue(date); state.Parameters.AddWithValue(FormulaVersion); await state.ExecuteNonQueryAsync(); }
        await using var finish=new NpgsqlCommand("UPDATE rotation.batch_runs SET status=CASE WHEN EXISTS(SELECT 1 FROM rotation.market_rotation_snapshots WHERE universe_id=$2 AND snapshot_date=$3 AND status='ok') THEN 'completed' ELSE 'insufficient_data' END,source_max_date=(SELECT max(trade_date) FROM market_data_public.adjusted_daily_bars_v1 WHERE trade_date <= $3),finished_at=now() WHERE universe_id=$2 AND snapshot_date=$3 AND formula_version=$4 RETURNING status",connection,tx);
        finish.Parameters.AddWithValue(runId); finish.Parameters.AddWithValue(id); finish.Parameters.AddWithValue(date); finish.Parameters.AddWithValue(FormulaVersion); var status=(string)(await finish.ExecuteScalarAsync())!; await tx.CommitAsync(); return Results.Ok(new { universeId=id, snapshotDate=date, status, formulaVersion=FormulaVersion });
    }
    catch(Exception e) { await tx.RollbackAsync(); await using var fail=db.CreateCommand("INSERT INTO rotation.batch_runs(id,universe_id,snapshot_date,formula_version,status,finished_at,error) VALUES($1,$2,$3,$4,'failed',now(),$5) ON CONFLICT(universe_id,snapshot_date,formula_version) DO UPDATE SET status='failed',finished_at=now(),error=excluded.error"); fail.Parameters.AddWithValue(runId); fail.Parameters.AddWithValue(id); fail.Parameters.AddWithValue(date); fail.Parameters.AddWithValue(FormulaVersion); fail.Parameters.AddWithValue(e.Message); await fail.ExecuteNonQueryAsync(); return Results.Json(new { error="calculation_failed" },statusCode:503); }
});

app.MapGet("/internal/rotation/monitor", async (string universe, DateOnly? date, NpgsqlDataSource db) =>
{
    var code=universe.Trim().ToUpperInvariant(); await using var command=db.CreateCommand(Sql.Monitor); command.Parameters.AddWithValue(code); command.Parameters.AddWithValue((object?)date??DBNull.Value);
    await using var reader=await command.ExecuteReaderAsync(); if(!await reader.ReadAsync()) return Results.NotFound();
    var id=reader.GetGuid(0); var universeCode=reader.GetString(1); var universeName=reader.GetString(2); var snapshotDate=reader.IsDBNull(3)?(DateOnly?)null:reader.GetFieldValue<DateOnly>(3); var state=reader.IsDBNull(4)?null:reader.GetString(4); var stateStatus=reader.IsDBNull(5)?"insufficient_data":reader.GetString(5); var breadth=reader.IsDBNull(6)?(decimal?)null:reader.GetDecimal(6); var benchmarkAbove=reader.IsDBNull(7)?(bool?)null:reader.GetBoolean(7); await reader.CloseAsync();
    await using var readConnection=await db.OpenConnectionAsync();
    var sectors=new List<object>(); await using(var q=new NpgsqlCommand("SELECT sector,member_count,available_count,above_ma20_percent,above_ma50_percent,above_ma200_percent,status FROM rotation.sector_breadth_snapshots WHERE universe_id=$1 AND snapshot_date=$2 ORDER BY above_ma20_percent DESC NULLS LAST,sector",readConnection)) { q.Parameters.AddWithValue(id); q.Parameters.AddWithValue((object?)snapshotDate??DBNull.Value); await using var r=await q.ExecuteReaderAsync(); while(await r.ReadAsync()) sectors.Add(new { sector=r.GetString(0),memberCount=r.GetInt32(1),availableCount=r.GetInt32(2),aboveMa20Percent=r.IsDBNull(3)?(decimal?)null:r.GetDecimal(3),aboveMa50Percent=r.IsDBNull(4)?(decimal?)null:r.GetDecimal(4),aboveMa200Percent=r.IsDBNull(5)?(decimal?)null:r.GetDecimal(5),status=r.GetString(6) }); }
    var etfs=new List<object>(); await using(var q=new NpgsqlCommand("SELECT s.symbol,u.label,s.sector,s.close,s.return_2w,s.return_1m,s.return_3m,s.rank_2w,s.percentile_2w,s.above_ma20,s.above_ma50,s.above_ma200,s.status FROM rotation.market_rotation_snapshots s JOIN rotation.market_rotation_universe_symbols u ON u.universe_id=s.universe_id AND u.symbol=s.symbol WHERE s.universe_id=$1 AND s.snapshot_date=$2 ORDER BY s.rank_2w NULLS LAST,u.sort_order",readConnection)) { q.Parameters.AddWithValue(id); q.Parameters.AddWithValue((object?)snapshotDate??DBNull.Value); await using var r=await q.ExecuteReaderAsync(); while(await r.ReadAsync()) etfs.Add(new { symbol=r.GetString(0),label=r.GetString(1),sector=r.IsDBNull(2)?null:r.GetString(2),close=NullableDecimal(r,3),return2w=NullableDecimal(r,4),return1m=NullableDecimal(r,5),return3m=NullableDecimal(r,6),rank2w=r.IsDBNull(7)?(int?)null:r.GetInt32(7),percentile2w=NullableDecimal(r,8),aboveMa20=NullableBool(r,9),aboveMa50=NullableBool(r,10),aboveMa200=NullableBool(r,11),status=r.GetString(12) }); }
    return Results.Ok(new { universe=new { id, code=universeCode, name=universeName }, snapshotDate, formulaVersion=FormulaVersion, status=snapshotDate is null?"insufficient_data":stateStatus, marketState=new { state, breadthPercent=breadth, benchmarkAboveMa200=benchmarkAbove, status=stateStatus }, sectorBreadth=sectors, etfs });
});

app.Run();

static decimal? NullableDecimal(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDecimal(i);
static bool? NullableBool(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetBoolean(i);
record UniverseWrite(string Code,string Name,string? RankScope);
record SymbolWrite(string Symbol,string Label,string? Sector,int? SortOrder);

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
