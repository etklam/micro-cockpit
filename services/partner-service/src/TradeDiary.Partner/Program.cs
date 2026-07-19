using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Partner") ?? throw new InvalidOperationException("Connection string 'Partner' is required.")));
builder.Services.AddHttpClient("identity", client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:Identity"] ?? "http://127.0.0.1:5100"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    o.RequireHttpsMetadata = false; // ponytail: local compose only; production config must use HTTPS.
    o.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(o =>
{
    var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build();
    o.DefaultPolicy = humanOnly;
    o.FallbackPolicy = humanOnly;
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
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
app.MapGet("/version", () => Results.Ok(new { service = "partner-service", version = "0.2.0" })).AllowAnonymous();

app.MapGet("/internal/partners", async (HttpRequest req, NpgsqlDataSource db, IHttpClientFactory httpFactory) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
        SELECT l.id, l.requester_user_id, l.partner_user_id, l.partner_type, l.status, l.created_at, l.updated_at,
               coalesce(mine.share_diaries, false), coalesce(theirs.share_diaries, false)
        FROM partner.partner_links l
        LEFT JOIN partner.partner_share_policies mine ON mine.link_id = l.id AND mine.owner_user_id = $1
        LEFT JOIN partner.partner_share_policies theirs ON theirs.link_id = l.id
          AND theirs.owner_user_id = CASE WHEN l.requester_user_id = $1 THEN l.partner_user_id ELSE l.requester_user_id END
        WHERE l.requester_user_id = $1 OR l.partner_user_id = $1
        ORDER BY l.created_at DESC
        """);
    c.Parameters.AddWithValue(user);
    await using var r = await c.ExecuteReaderAsync();
    var items = new List<PartnerLinkView>();
    var otherIds = new HashSet<Guid>();
    while (await r.ReadAsync())
    {
        var link = ReadLink(r, user);
        items.Add(link);
        otherIds.Add(link.OtherUserId);
    }
    var names = await ResolveDisplayNames(httpFactory, req, otherIds);
    for (var i = 0; i < items.Count; i++)
    {
        var item = items[i];
        names.TryGetValue(item.OtherUserId, out var displayName);
        items[i] = item with { PartnerDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Partner" : displayName! };
    }
    return Results.Ok(new CollectionResponse<PartnerLinkView>(items));
})
.Produces<CollectionResponse<PartnerLinkView>>(200).ProducesProblem(401);

app.MapPost("/internal/partners", async (LinkWrite x, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    if (x.PartnerUserId == user || x.PartnerType is not ("human" or "agent")) return Results.Problem("invalid_partner", statusCode: 400);
    var id = Guid.NewGuid();
    await using var c = db.CreateCommand("""
        INSERT INTO partner.partner_links(id,requester_user_id,partner_user_id,partner_type,status)
        VALUES($1,$2,$3,$4,'pending')
        RETURNING id,requester_user_id,partner_user_id,partner_type,status,created_at,updated_at
        """);
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user); c.Parameters.AddWithValue(x.PartnerUserId); c.Parameters.AddWithValue(x.PartnerType);
    try
    {
        await using var r = await c.ExecuteReaderAsync();
        await r.ReadAsync();
        return Results.Created($"/internal/partners/{id}", Read(r));
    }
    catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation)
    {
        return Results.Problem("link_exists", statusCode: 409);
    }
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
    await using var c = db.CreateCommand("UPDATE partner.partner_links SET status='revoked',updated_at=now() WHERE id=$1 AND status <> 'revoked' AND (requester_user_id=$2 OR partner_user_id=$2)");
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapPut("/internal/partners/{id:guid}/share-policy", async (Guid id, SharePolicyWrite x, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
        INSERT INTO partner.partner_share_policies(link_id,owner_user_id,share_diaries,share_transactions,share_performance)
        SELECT id,$2,$3,coalesce((SELECT share_transactions FROM partner.partner_share_policies WHERE link_id=$1 AND owner_user_id=$2),false),
               coalesce((SELECT share_performance FROM partner.partner_share_policies WHERE link_id=$1 AND owner_user_id=$2),false)
        FROM partner.partner_links WHERE id=$1 AND status='accepted' AND (requester_user_id=$2 OR partner_user_id=$2)
        ON CONFLICT(link_id,owner_user_id) DO UPDATE SET share_diaries=$3,updated_at=now()
        """);
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user); c.Parameters.AddWithValue(x.ShareDiaries);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapGet("/internal/partners/{ownerId:guid}/authorization", async (Guid ownerId, string resource, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var viewer)) return Results.Unauthorized();
    if (ownerId == viewer) return Results.Ok(new AuthorizationResponse(true));
    var column = resource switch { "diary" => "share_diaries", "transaction" => "share_transactions", "performance" => "share_performance", _ => null };
    if (column is null) return Results.Problem("invalid_resource", statusCode: 400);
    await using var c = db.CreateCommand($"""
        SELECT coalesce(p.{column}, false)
        FROM partner.partner_links l
        JOIN partner.partner_share_policies p ON p.link_id = l.id AND p.owner_user_id = $1
        WHERE l.status = 'accepted'
          AND ((l.requester_user_id = $1 AND l.partner_user_id = $2) OR (l.partner_user_id = $1 AND l.requester_user_id = $2))
        """);
    c.Parameters.AddWithValue(ownerId); c.Parameters.AddWithValue(viewer);
    return Results.Ok(new AuthorizationResponse(await c.ExecuteScalarAsync() is true));
})
.Produces<AuthorizationResponse>(200).ProducesProblem(400).ProducesProblem(401);

app.MapGet("/internal/partners/{id:guid}/summary", async (Guid id, HttpRequest req, NpgsqlDataSource db, IHttpClientFactory httpFactory) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
        SELECT l.id, l.requester_user_id, l.partner_user_id, l.partner_type, l.status, l.created_at, l.updated_at,
               coalesce(mine.share_diaries, false), coalesce(theirs.share_diaries, false)
        FROM partner.partner_links l
        LEFT JOIN partner.partner_share_policies mine ON mine.link_id = l.id AND mine.owner_user_id = $2
        LEFT JOIN partner.partner_share_policies theirs ON theirs.link_id = l.id
          AND theirs.owner_user_id = CASE WHEN l.requester_user_id = $2 THEN l.partner_user_id ELSE l.requester_user_id END
        WHERE l.id = $1 AND (l.requester_user_id = $2 OR l.partner_user_id = $2)
        """);
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user);
    await using var r = await c.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.Problem("not_found", statusCode: 404);
    var link = ReadLink(r, user);
    var names = await ResolveDisplayNames(httpFactory, req, [link.OtherUserId]);
    names.TryGetValue(link.OtherUserId, out var displayName);
    return Results.Ok(link with { PartnerDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Partner" : displayName! });
})
.Produces<PartnerLinkView>(200).ProducesProblem(401).ProducesProblem(404);

app.MapPost("/internal/partners/invitations", async (HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    var raw = CreateInvitationCode();
    var hash = HashCode(raw);
    var id = Guid.NewGuid();
    var expiresAt = DateTime.UtcNow.AddDays(7);
    await using var c = db.CreateCommand("""
        INSERT INTO partner.partner_invitations(id, creator_user_id, code_hash, status, expires_at)
        VALUES ($1,$2,$3,'pending',$4)
        """);
    c.Parameters.AddWithValue(id);
    c.Parameters.AddWithValue(user);
    c.Parameters.AddWithValue(hash);
    c.Parameters.AddWithValue(expiresAt);
    await c.ExecuteNonQueryAsync();
    return Results.Created($"/internal/partners/invitations/{id}", new InvitationCreatedResponse(id, raw, expiresAt));
})
.Produces<InvitationCreatedResponse>(201).ProducesProblem(401);

app.MapGet("/internal/partners/invitations", async (HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
        SELECT id, status, expires_at, created_at
        FROM partner.partner_invitations
        WHERE creator_user_id = $1 AND status = 'pending' AND expires_at > now()
        ORDER BY created_at DESC
        """);
    c.Parameters.AddWithValue(user);
    await using var r = await c.ExecuteReaderAsync();
    var items = new List<InvitationListItem>();
    while (await r.ReadAsync())
        items.Add(new InvitationListItem(r.GetGuid(0), r.GetString(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc), DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
    return Results.Ok(new CollectionResponse<InvitationListItem>(items));
})
.Produces<CollectionResponse<InvitationListItem>>(200).ProducesProblem(401);

app.MapDelete("/internal/partners/invitations/{id:guid}", async (Guid id, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    await using var c = db.CreateCommand("""
        UPDATE partner.partner_invitations
        SET status = 'revoked', revoked_at = now()
        WHERE id = $1 AND creator_user_id = $2 AND status = 'pending' AND expires_at > now()
        """);
    c.Parameters.AddWithValue(id); c.Parameters.AddWithValue(user);
    return await c.ExecuteNonQueryAsync() == 0 ? Results.Problem("not_found", statusCode: 404) : Results.NoContent();
})
.Produces(204).ProducesProblem(401).ProducesProblem(404);

app.MapPost("/internal/partners/invitations/redeem", async (RedeemInvitation body, HttpRequest req, NpgsqlDataSource db) =>
{
    if (!User(req, out var user)) return Results.Unauthorized();
    if (!TryNormalizeCode(body.Code, out var code)) return Results.Problem("invalid_invitation", statusCode: 400);
    var hash = HashCode(code);

    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();
    await using var select = new NpgsqlCommand("""
        SELECT id, creator_user_id, status, expires_at
        FROM partner.partner_invitations
        WHERE code_hash = $1
        FOR UPDATE
        """, connection, tx);
    select.Parameters.AddWithValue(hash);
    Guid invitationId;
    Guid creatorId;
    string status;
    DateTime expiresAt;
    await using (var reader = await select.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
        {
            await tx.RollbackAsync();
            return Results.Problem("invalid_invitation", statusCode: 400);
        }
        invitationId = reader.GetGuid(0);
        creatorId = reader.GetGuid(1);
        status = reader.GetString(2);
        expiresAt = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
    }

    if (creatorId == user || status != "pending" || expiresAt <= DateTime.UtcNow)
    {
        await tx.RollbackAsync();
        return Results.Problem("invalid_invitation", statusCode: 400);
    }

    var linkId = Guid.NewGuid();
    await using (var insert = new NpgsqlCommand("""
        INSERT INTO partner.partner_links(id, requester_user_id, partner_user_id, partner_type, status)
        VALUES ($1, $2, $3, 'human', 'accepted')
        """, connection, tx))
    {
        insert.Parameters.AddWithValue(linkId);
        insert.Parameters.AddWithValue(creatorId);
        insert.Parameters.AddWithValue(user);
        try { await insert.ExecuteNonQueryAsync(); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync();
            return Results.Problem("invalid_invitation", statusCode: 400);
        }
    }

    await using (var update = new NpgsqlCommand("""
        UPDATE partner.partner_invitations
        SET status = 'redeemed', redeemed_at = now(), redeemed_by_user_id = $2, created_link_id = $3
        WHERE id = $1 AND status = 'pending'
        """, connection, tx))
    {
        update.Parameters.AddWithValue(invitationId);
        update.Parameters.AddWithValue(user);
        update.Parameters.AddWithValue(linkId);
        if (await update.ExecuteNonQueryAsync() != 1)
        {
            await tx.RollbackAsync();
            return Results.Problem("invalid_invitation", statusCode: 400);
        }
    }

    await tx.CommitAsync();
    return Results.Ok(new RedeemInvitationResponse(linkId));
})
.Produces<RedeemInvitationResponse>(200).ProducesProblem(400).ProducesProblem(401);

app.Run();

static bool User(HttpRequest r, out Guid id) => Guid.TryParse(r.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

static Link Read(NpgsqlDataReader r) => new(
    r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), r.GetString(4),
    DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
    DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc));

static PartnerLinkView ReadLink(NpgsqlDataReader r, Guid viewer)
{
    var requester = r.GetGuid(1);
    var partner = r.GetGuid(2);
    var other = requester == viewer ? partner : requester;
    return new PartnerLinkView(
        r.GetGuid(0),
        other,
        r.GetString(3),
        r.GetString(4),
        DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
        DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc),
        InitiatedByMe: requester == viewer,
        MyShareDiaries: r.GetBoolean(7),
        PartnerShareDiaries: r.GetBoolean(8),
        PartnerDisplayName: "");
}

static string CreateInvitationCode()
{
    Span<byte> bytes = stackalloc byte[18];
    RandomNumberGenerator.Fill(bytes);
    return Base64Url(bytes);
}

static byte[] HashCode(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

static bool TryNormalizeCode(string? raw, out string code)
{
    code = (raw ?? string.Empty).Trim();
    // 18 random bytes → 24 base64url chars; reject empty / oversized junk.
    return code.Length is >= 16 and <= 64;
}

static string Base64Url(ReadOnlySpan<byte> bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static async Task<Dictionary<Guid, string>> ResolveDisplayNames(IHttpClientFactory httpFactory, HttpRequest request, IEnumerable<Guid> ids)
{
    var result = new Dictionary<Guid, string>();
    var list = ids.Distinct().ToArray();
    if (list.Length == 0) return result;
    var client = httpFactory.CreateClient("identity");
    using var message = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/display-names?ids={string.Join(",", list)}");
    var auth = request.Headers.Authorization.ToString();
    if (!string.IsNullOrWhiteSpace(auth)) message.Headers.TryAddWithoutValidation("Authorization", auth);
    try
    {
        using var response = await client.SendAsync(message);
        if (!response.IsSuccessStatusCode) return result;
        var payload = await response.Content.ReadFromJsonAsync<CollectionResponse<DisplayNameItem>>();
        if (payload?.Items is null) return result;
        foreach (var item in payload.Items) result[item.UserId] = item.DisplayName;
    }
    catch (HttpRequestException) { /* display names degrade to placeholder */ }
    catch (TaskCanceledException) { }
    return result;
}

record LinkWrite(Guid PartnerUserId, string PartnerType);
record SharePolicyWrite(bool ShareDiaries);
record SharePolicy(bool ShareDiaries, bool ShareTransactions, bool SharePerformance);
record Link(Guid Id, Guid RequesterUserId, Guid PartnerUserId, string PartnerType, string Status, DateTime CreatedAt, DateTime UpdatedAt);
record PartnerLinkView(
    Guid Id,
    Guid OtherUserId,
    string PartnerType,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool InitiatedByMe,
    bool MyShareDiaries,
    bool PartnerShareDiaries,
    string PartnerDisplayName);
record AuthorizationResponse(bool Allowed);
record CollectionResponse<T>(List<T> Items);
record InvitationCreatedResponse(Guid Id, string Code, DateTime ExpiresAt);
record InvitationListItem(Guid Id, string Status, DateTime ExpiresAt, DateTime CreatedAt);
record RedeemInvitation(string Code);
record RedeemInvitationResponse(Guid LinkId);
record DisplayNameItem(Guid UserId, string DisplayName);

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

public partial class Program;
