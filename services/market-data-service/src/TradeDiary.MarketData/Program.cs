using Npgsql;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("MarketData") ?? "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status="ready" }); } catch { return Results.Json(new { status="not_ready" }, statusCode:503); } });
app.MapGet("/version", () => Results.Ok(new { service="market-data-service", version="0.1.0", contract="v1" }));

app.MapPut("/internal/admin/symbols/{raw}", async (string raw, SymbolWrite input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request, config)) return Results.Unauthorized();
    var symbol=raw.Trim().ToUpperInvariant();
    if (symbol.Length is < 1 or > 24 || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Exchange) || input.Currency.Trim().Length != 3) return Results.BadRequest(new { error="invalid_symbol" });
    try { TimeZoneInfo.FindSystemTimeZoneById(input.Timezone); } catch { return Results.BadRequest(new { error="invalid_timezone" }); }
    await using var cmd=db.CreateCommand("INSERT INTO market.symbols(symbol,name,exchange,currency,timezone,active) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(symbol) DO UPDATE SET name=$2,exchange=$3,currency=$4,timezone=$5,active=$6,updated_at=now()");
    cmd.Parameters.AddWithValue(symbol); cmd.Parameters.AddWithValue(input.Name.Trim()); cmd.Parameters.AddWithValue(input.Exchange.Trim()); cmd.Parameters.AddWithValue(input.Currency.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue(input.Timezone); cmd.Parameters.AddWithValue(input.Active); await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});
app.MapPost("/internal/admin/provider-runs", async (ProviderRunWrite input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Unauthorized(); if (string.IsNullOrWhiteSpace(input.Provider)) return Results.BadRequest(new { error="invalid_provider" });
    var id=Guid.NewGuid(); await using var cmd=db.CreateCommand("INSERT INTO market.provider_runs(id,provider,started_at,status) VALUES($1,$2,now(),'running')"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(input.Provider.Trim()); await cmd.ExecuteNonQueryAsync(); return Results.Created($"/internal/admin/provider-runs/{id}",new { id });
});
app.MapPut("/internal/admin/provider-runs/{id:guid}/bars", async (Guid id, List<BarWrite> bars, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Unauthorized(); if (bars.Count is 0 or > 5000 || bars.Any(x => !ValidBar(x))) return Results.BadRequest(new { error="invalid_bars" });
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using var owner=new NpgsqlCommand("SELECT provider FROM market.provider_runs WHERE id=$1 AND status='running' FOR UPDATE",connection,tx); owner.Parameters.AddWithValue(id); var provider=(string?)await owner.ExecuteScalarAsync(); if (provider is null) return Results.NotFound();
    foreach(var bar in bars) { var symbol=bar.Symbol.Trim().ToUpperInvariant(); await using var cmd=new NpgsqlCommand("INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9) ON CONFLICT(symbol,trading_date) DO UPDATE SET open=$3,high=$4,low=$5,close=$6,volume=$7,provider=$8,provider_run_id=$9,ingested_at=now(),published_at=NULL",connection,tx); cmd.Parameters.AddWithValue(symbol); cmd.Parameters.AddWithValue(bar.TradingDate); cmd.Parameters.AddWithValue(bar.Open); cmd.Parameters.AddWithValue(bar.High); cmd.Parameters.AddWithValue(bar.Low); cmd.Parameters.AddWithValue(bar.Close); cmd.Parameters.AddWithValue(bar.Volume); cmd.Parameters.AddWithValue(provider); cmd.Parameters.AddWithValue(id); try { await cmd.ExecuteNonQueryAsync(); } catch(PostgresException e) when(e.SqlState=="23503") { return Results.BadRequest(new { error="unknown_symbol", symbol }); } }
    await using var count=new NpgsqlCommand("UPDATE market.provider_runs SET rows_received=rows_received+$2 WHERE id=$1",connection,tx); count.Parameters.AddWithValue(id); count.Parameters.AddWithValue(bars.Count); await count.ExecuteNonQueryAsync(); await tx.CommitAsync(); return Results.NoContent();
});
app.MapPost("/internal/admin/provider-runs/{id:guid}/complete", async (Guid id, CompleteRun input, HttpRequest request, NpgsqlDataSource db, IConfiguration config) =>
{
    if (!Admin(request,config)) return Results.Unauthorized(); if (input.Status is not ("succeeded" or "failed")) return Results.BadRequest(new { error="invalid_status" });
    await using var connection=await db.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync();
    await using var done=new NpgsqlCommand("UPDATE market.provider_runs SET status=$2,error=$3,completed_at=now() WHERE id=$1 AND status='running'",connection,tx); done.Parameters.AddWithValue(id); done.Parameters.AddWithValue(input.Status); done.Parameters.AddWithValue((object?)input.Error??DBNull.Value); if(await done.ExecuteNonQueryAsync()==0) return Results.NotFound();
    if(input.Status=="succeeded") { await using var publish=new NpgsqlCommand("UPDATE market.daily_bars SET published_at=now() WHERE provider_run_id=$1",connection,tx); publish.Parameters.AddWithValue(id); await publish.ExecuteNonQueryAsync(); }
    await tx.CommitAsync(); return Results.NoContent();
});

app.MapGet("/internal/v1/symbols", async (NpgsqlDataSource db) => { await using var cmd=db.CreateCommand("SELECT symbol,name,exchange,currency,timezone FROM market.published_symbols_v1 ORDER BY symbol"); await using var r=await cmd.ExecuteReaderAsync(); var items=new List<object>(); while(await r.ReadAsync()) items.Add(new { symbol=r.GetString(0),name=r.GetString(1),exchange=r.GetString(2),currency=r.GetString(3),timezone=r.GetString(4) }); return Results.Ok(new { contractVersion=1,items }); });
app.MapGet("/internal/v1/bars/{raw}", async (string raw, DateOnly? from, DateOnly? to, NpgsqlDataSource db) => { var symbol=raw.Trim().ToUpperInvariant(); var end=to??DateOnly.FromDateTime(DateTime.UtcNow); var start=from??end.AddDays(-365); if(end<start || end.DayNumber-start.DayNumber>3660) return Results.BadRequest(new { error="invalid_date_range" }); await using var cmd=db.CreateCommand("SELECT trading_date,open,high,low,close,volume,provider,published_at FROM market.published_daily_bars_v1 WHERE symbol=$1 AND trading_date BETWEEN $2 AND $3 ORDER BY trading_date"); cmd.Parameters.AddWithValue(symbol);cmd.Parameters.AddWithValue(start);cmd.Parameters.AddWithValue(end);await using var r=await cmd.ExecuteReaderAsync();var items=new List<object>();while(await r.ReadAsync())items.Add(new { tradingDate=r.GetFieldValue<DateOnly>(0),open=r.GetDecimal(1),high=r.GetDecimal(2),low=r.GetDecimal(3),close=r.GetDecimal(4),volume=r.GetDecimal(5),provider=r.GetString(6),publishedAt=r.GetDateTime(7) });return Results.Ok(new { contractVersion=1,symbol,items }); });
app.MapGet("/internal/v1/providers/health", async (NpgsqlDataSource db) => { await using var cmd=db.CreateCommand("SELECT provider,last_success_at,healthy FROM market.published_provider_health_v1 ORDER BY provider");await using var r=await cmd.ExecuteReaderAsync();var items=new List<object>();while(await r.ReadAsync())items.Add(new { provider=r.GetString(0),lastSuccessAt=r.GetDateTime(1),healthy=r.GetBoolean(2) });return Results.Ok(new { contractVersion=1,healthy=items.Count>0 && items.All(x=>(bool)x.GetType().GetProperty("healthy")!.GetValue(x)!),items }); });
app.Run();

static bool Admin(HttpRequest request,IConfiguration config){var a=Encoding.UTF8.GetBytes(request.Headers["X-Service-Key"].ToString());var b=Encoding.UTF8.GetBytes(config["Internal:ServiceKey"]??"local-service-key");return a.Length==b.Length&&CryptographicOperations.FixedTimeEquals(a,b);}
static bool ValidBar(BarWrite x)=>!string.IsNullOrWhiteSpace(x.Symbol)&&x.Open>=0&&x.Low>=0&&x.Close>=0&&x.Volume>=0&&x.High>=Math.Max(x.Open,Math.Max(x.Low,x.Close))&&x.Low<=Math.Min(x.Open,Math.Min(x.High,x.Close));
record SymbolWrite(string Name,string Exchange,string Currency,string Timezone,bool Active=true);
record ProviderRunWrite(string Provider);
record CompleteRun(string Status,string? Error);
record BarWrite(string Symbol,DateOnly TradingDate,decimal Open,decimal High,decimal Low,decimal Close,decimal Volume);
