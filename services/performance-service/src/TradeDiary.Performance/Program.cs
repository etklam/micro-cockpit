using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Performance") ??
    "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); }
    catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); }
}).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "performance-service", version = "0.1.0" })).AllowAnonymous();

app.MapPut("/internal/daily-performances/{date}", async (DateOnly date, PerformanceWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (input.CapitalBase is <= 0) return Results.BadRequest(new { error = "capital_base_must_be_positive" });
    await using var command = db.CreateCommand("""
        INSERT INTO performance.daily_performances (user_id, local_date, pnl_amount, capital_base, note)
        VALUES ($1,$2,$3,$4,$5)
        ON CONFLICT (user_id, local_date) DO UPDATE SET
          pnl_amount=excluded.pnl_amount, capital_base=excluded.capital_base, note=excluded.note, updated_at=now()
        """);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(date);
    command.Parameters.AddWithValue(input.PnlAmount);
    command.Parameters.AddWithValue((object?)input.CapitalBase ?? DBNull.Value);
    command.Parameters.AddWithValue(input.Note ?? "");
    await command.ExecuteNonQueryAsync();
    return Results.Ok(ToResponse(date, input.PnlAmount, input.CapitalBase, input.Note ?? ""));
});

app.MapGet("/internal/performance/day/{date}", async (DateOnly date, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("SELECT pnl_amount, capital_base, note FROM performance.daily_performances WHERE user_id=$1 AND local_date=$2");
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(date);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound(); // Missing is not zero.
    return Results.Ok(ToResponse(date, reader.GetDecimal(0), reader.IsDBNull(1) ? null : reader.GetDecimal(1), reader.GetString(2)));
});

app.MapGet("/internal/daily-performances", async (DateOnly from, DateOnly to, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (to < from || to.DayNumber - from.DayNumber > 62) return Results.BadRequest(new { error = "invalid_date_range" });
    await using var command = db.CreateCommand("SELECT local_date,pnl_amount,capital_base,note FROM performance.daily_performances WHERE user_id=$1 AND local_date BETWEEN $2 AND $3 ORDER BY local_date");
    command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to);
    await using var reader = await command.ExecuteReaderAsync(); var items = new List<object>();
    while (await reader.ReadAsync()) items.Add(ToResponse(reader.GetFieldValue<DateOnly>(0), reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.GetString(3)));
    return Results.Ok(new { items });
});

app.MapDelete("/internal/daily-performances/{date}", async (DateOnly date, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("DELETE FROM performance.daily_performances WHERE user_id=$1 AND local_date=$2");
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(date);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapGet("/internal/performance/month-summary", async (int year, int month, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (month is < 1 or > 12) return Results.BadRequest(new { error = "invalid_month" });
    var start = new DateOnly(year, month, 1);
    var end = start.AddMonths(1);
    await using var command = db.CreateCommand("""
        SELECT coalesce(sum(pnl_amount),0), count(*), count(*) filter (where pnl_amount > 0),
               count(*) filter (where pnl_amount < 0), count(*) filter (where pnl_amount = 0),
               max(pnl_amount), min(pnl_amount)
        FROM performance.daily_performances WHERE user_id=$1 AND local_date >= $2 AND local_date < $3
        """);
    command.Parameters.AddWithValue(userId);
    command.Parameters.AddWithValue(start);
    command.Parameters.AddWithValue(end);
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new {
        year, month, total = reader.GetDecimal(0), recordedDays = reader.GetInt64(1),
        profitDays = reader.GetInt64(2), lossDays = reader.GetInt64(3), flatDays = reader.GetInt64(4),
        bestDay = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5), worstDay = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6)
    });
});

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) =>
    Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

static object ToResponse(DateOnly date, decimal amount, decimal? capital, string note) => new
{
    localDate = date,
    pnlAmount = amount,
    capitalBase = capital,
    pnlPercent = capital is null ? (decimal?)null : decimal.Round(amount / capital.Value * 100, 4),
    note
};

record PerformanceWrite(decimal PnlAmount, decimal? CapitalBase, string? Note);
