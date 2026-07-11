using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Discipline") ?? "Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "discipline-service", version = "0.1.0" })).AllowAnonymous();

app.MapGet("/internal/disciplines", async (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    return Results.Ok(new { items = await List(db, userId) });
});

app.MapPost("/internal/disciplines", async (DisciplineWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Content)) return Results.BadRequest(new { error = "content_required" });
    var id = Guid.NewGuid();
    await using var command = db.CreateCommand("""
        INSERT INTO discipline.disciplines(id,user_id,content,position)
        VALUES($1,$2,$3,(SELECT coalesce(max(position)+1,0) FROM discipline.disciplines WHERE user_id=$2))
        RETURNING id,content,position,created_at,updated_at
        """);
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(input.Content.Trim());
    await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
    return Results.Created($"/internal/disciplines/{id}", Read(reader));
});

app.MapPut("/internal/disciplines/{id:guid}", async (Guid id, DisciplineWrite input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Content)) return Results.BadRequest(new { error = "content_required" });
    await using var command = db.CreateCommand("UPDATE discipline.disciplines SET content=$3,updated_at=now() WHERE id=$1 AND user_id=$2");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(input.Content.Trim());
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/internal/disciplines/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    await using var command = db.CreateCommand("DELETE FROM discipline.disciplines WHERE id=$1 AND user_id=$2");
    command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(userId);
    return await command.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapPost("/internal/disciplines/reorder", async (ReorderRequest input, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    if (input.Ids.Count != input.Ids.Distinct().Count()) return Results.BadRequest(new { error = "duplicate_id" });
    await using var connection = await db.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
    await using var select = new NpgsqlCommand("SELECT id FROM discipline.disciplines WHERE user_id=$1 FOR UPDATE", connection, transaction);
    select.Parameters.AddWithValue(userId); await using var reader = await select.ExecuteReaderAsync();
    var current = new HashSet<Guid>(); while (await reader.ReadAsync()) current.Add(reader.GetGuid(0)); await reader.CloseAsync();
    if (!current.SetEquals(input.Ids)) { await transaction.RollbackAsync(); return Results.BadRequest(new { error = "ids_must_match_all_disciplines" }); }
    for (var position = 0; position < input.Ids.Count; position++)
    {
        await using var update = new NpgsqlCommand("UPDATE discipline.disciplines SET position=$1,updated_at=now() WHERE id=$2 AND user_id=$3", connection, transaction);
        update.Parameters.AddWithValue(position); update.Parameters.AddWithValue(input.Ids[position]); update.Parameters.AddWithValue(userId); await update.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync(); return Results.NoContent();
});

app.MapGet("/internal/disciplines/today", async (DateOnly? date, HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var items = await List(db, userId); if (items.Count == 0) return Results.NotFound();
    var timezoneId = request.HttpContext.User.FindFirst("timezone")?.Value ?? "UTC";
    TimeZoneInfo timezone;
    try { timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); } catch { return Results.BadRequest(new { error = "invalid_timezone" }); }
    var localDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{localDate:yyyy-MM-dd}"));
    var index = (int)(BitConverter.ToUInt64(hash, 0) % (ulong)items.Count);
    return Results.Ok(items[index]);
});

app.MapGet("/internal/disciplines/random", async (HttpRequest request, NpgsqlDataSource db) =>
{
    if (!TryUser(request, out var userId)) return Results.Unauthorized();
    var items = await List(db, userId);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items[RandomNumberGenerator.GetInt32(items.Count)]);
});

app.Run();

static bool TryUser(HttpRequest request, out Guid userId) => Guid.TryParse(request.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);
static async Task<List<DisciplineResponse>> List(NpgsqlDataSource db, Guid userId)
{
    await using var command = db.CreateCommand("SELECT id,content,position,created_at,updated_at FROM discipline.disciplines WHERE user_id=$1 ORDER BY position,id");
    command.Parameters.AddWithValue(userId); await using var reader = await command.ExecuteReaderAsync();
    var items = new List<DisciplineResponse>(); while (await reader.ReadAsync()) items.Add(Read(reader)); return items;
}
static DisciplineResponse Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetDateTime(3), reader.GetDateTime(4));
record DisciplineWrite(string Content);
record ReorderRequest(List<Guid> Ids);
record DisciplineResponse(Guid Id, string Content, int Position, DateTime CreatedAt, DateTime UpdatedAt);
