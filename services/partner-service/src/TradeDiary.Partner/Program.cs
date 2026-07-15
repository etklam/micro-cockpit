using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Partner") ?? throw new InvalidOperationException("Connection string 'Partner' is required.")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    o.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    o.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(o => { var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build(); o.DefaultPolicy = humanOnly; o.FallbackPolicy = humanOnly; });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<SecuritySchemesTransformer>();
    options.AddOperationTransformer<SecurityRequirementTransformer>();
});
var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (NpgsqlDataSource db) => { try { await db.OpenConnectionAsync(); return Results.Ok(new { status = "ready" }); } catch { return Results.Json(new { status = "not_ready" }, statusCode: 503); } }).AllowAnonymous();
app.MapGet("/version", () => Results.Ok(new { service = "partner-service", version = "0.1.0" })).AllowAnonymous();

app.MapGet("/internal/partners", async (HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("SELECT id,requester_user_id,partner_user_id,partner_type,status,created_at,updated_at FROM partner.partner_links WHERE requester_user_id=$1 OR partner_user_id=$1 ORDER BY created_at DESC");
    c.Parameters.AddWithValue(user);
    await using var r = await c.ExecuteReaderAsync(); var items = new List<Link>();
    while (await r.ReadAsync()) items.Add(Read(r));
    return Results.Ok(new CollectionResponse<Link>(items));
})
.Produces<CollectionResponse<Link>>(200).ProducesProblem(401);

app.MapPost("/internal/partners", async (LinkWrite x, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    if (x.PartnerUserId == user || x.PartnerType is not ("human" or "agent")) return Results.Problem("invalid_partner", statusCode: 400);
    var id = Guid.NewGuid();
    await using var c = db.CreateCommand("INSERT INTO partner.partner_links(id,requester_user_id,partner_user_id,partner_type,status) VALUES($1,$2,$3,$4,'pending') RETURNING id,requester_user_id,partner_user_id,partner_type,status,created_at,updated_at");
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user); c.Parameters.AddWithValue(x.PartnerUserId); c.Parameters.AddWithValue(x.PartnerType);
    try { await using var r = await c.ExecuteReaderAsync(); await r.ReadAsync(); return Results.Created($"/internal/partners/{id}", Read(r)); }
    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { return Results.Problem("link_exists", statusCode: 409); }
})
.Produces<Link>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

app.MapPost("/internal/partners/{id:guid}/accept", async (Guid id, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("UPDATE partner.partner_links SET status='accepted',updated_at=now() WHERE id=$1 AND partner_user_id=$2 AND status='pending'");
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapDelete("/internal/partners/{id:guid}", async (Guid id, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("UPDATE partner.partner_links SET status='revoked',updated_at=now() WHERE id=$1 AND (requester_user_id=$2 OR partner_user_id=$2)");
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapPut("/internal/partners/{id:guid}/share-policy", async (Guid id, SharePolicy x, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
    INSERT INTO partner.partner_share_policies(link_id,owner_user_id,share_diaries,share_transactions,share_performance)
    SELECT id,$2,$3,$4,$5 FROM partner.partner_links WHERE id=$1 AND status='accepted' AND (requester_user_id=$2 OR partner_user_id=$2)
    ON CONFLICT(link_id,owner_user_id) DO UPDATE SET share_diaries=$3,share_transactions=$4,share_performance=$5,updated_at=now()
    """);
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user); c.Parameters.AddWithValue(x.ShareDiaries); c.Parameters.AddWithValue(x.ShareTransactions); c.Parameters.AddWithValue(x.SharePerformance);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/partners/{ownerId:guid}/authorization", async (Guid ownerId, string resource, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var viewer)) return Results.Unauthorized();
    if (ownerId == viewer) return Results.Ok(new AuthorizationResponse(true));
    var column = resource switch { "diary" => "share_diaries", "transaction" => "share_transactions", "performance" => "share_performance", _ => null };
    if (column is null) return Results.Problem("invalid_resource", statusCode: 400);
    await using var c = db.CreateCommand($"SELECT coalesce(p.{column},false) FROM partner.partner_links l JOIN partner.partner_share_policies p ON p.link_id=l.id AND p.owner_user_id=$1 WHERE l.status='accepted' AND ((l.requester_user_id=$1 AND l.partner_user_id=$2) OR (l.partner_user_id=$1 AND l.requester_user_id=$2))");
    c.Parameters.AddWithValue(ownerId); c.Parameters.AddWithValue(viewer);
    return Results.Ok(new AuthorizationResponse(await c.ExecuteScalarAsync() is true));
})
.Produces<AuthorizationResponse>(200).ProducesProblem(400).ProducesProblem(401);

app.Run();

static bool User(HttpRequest r, out Guid id) => Guid.TryParse(r.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);
static Link Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetDateTime(6));

record LinkWrite(Guid PartnerUserId, string PartnerType);
record SharePolicy(bool ShareDiaries, bool ShareTransactions, bool SharePerformance);
record Link(Guid Id, Guid RequesterUserId, Guid PartnerUserId, string PartnerType, string Status, DateTime CreatedAt, DateTime UpdatedAt);
record AuthorizationResponse(bool Allowed);
record CollectionResponse<T>(List<T> Items);

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
