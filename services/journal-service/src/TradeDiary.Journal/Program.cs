using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeDiary.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Journal") ?? throw new InvalidOperationException("Connection string 'Journal' is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(options =>
{
    TradeDiaryPolicies.Configure(options);
    options.AddPolicy("serviceKey", policy => policy.RequireAssertion(context =>
        context.Resource is HttpContext http && TrackedInstrumentEndpoints.HasServiceKey(http, builder.Configuration)));
});
builder.Services.AddHttpClient("identity", client => client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://127.0.0.1:5100"));
builder.Services.AddHttpClient("market-data", client => client.BaseAddress = new Uri(builder.Configuration["Services:MarketData"] ?? "http://127.0.0.1:5106"));
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
TrackedInstrumentEndpoints.Map(app);
AccessGrantEndpoints.Map(app);

// All aggregate routes live under one authorized group and are mapped from per-aggregate
// endpoint modules. Program.cs stays a thin composition root; route logic lives in the modules.
var journal = app.MapGroup("/internal").RequireAuthorization(TradeDiaryPolicies.JournalAccess);
ObservationEndpoints.Map(journal);
ExpectationEndpoints.Map(journal);
ExpectationReviewEndpoints.Map(journal);
ComparisonEndpoints.Map(journal);
ActionDecisionEndpoints.Map(journal);
WatchlistEndpoints.Map(journal);
PatternReviewEndpoints.Map(journal);
AccountDataEndpoints.Map(journal);

app.Run();

public partial class Program;
