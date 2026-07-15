using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Content")??"Host=localhost;Port=5433;Database=trade_diary;Username=trade_diary;Password=local_only"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration";o.RequireHttpsMetadata=false;o.Audience="trade-diary-services";});
builder.Services.AddAuthorization(o=>{var humanOnly=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context=>context.User.FindFirst("account_type")?.Value!="agent").Build();o.DefaultPolicy=humanOnly;o.FallbackPolicy=humanOnly;});
builder.Services.AddOpenApi(options=>{options.AddDocumentTransformer<SecuritySchemesTransformer>();options.AddOperationTransformer<SecurityRequirementTransformer>();});
var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();app.MapGet("/health/ready",async(NpgsqlDataSource db)=>{try{await db.OpenConnectionAsync();return Results.Ok(new{status="ready"});}catch{return Results.Json(new{status="not_ready"},statusCode:503);}}).AllowAnonymous();app.MapGet("/version",()=>Results.Ok(new{service="content-service",version="0.1.0"})).AllowAnonymous();
app.MapGet("/internal/posts",async(NpgsqlDataSource db)=>{await using var c=db.CreateCommand("SELECT id,slug,title,body,published_at FROM content.posts WHERE status='published' ORDER BY published_at DESC");await using var r=await c.ExecuteReaderAsync();var items=new List<PostResponse>();while(await r.ReadAsync())items.Add(ReadPost(r));return Results.Ok(new CollectionResponse<PostResponse>(items));}).AllowAnonymous()
.Produces<CollectionResponse<PostResponse>>(200);
app.MapGet("/internal/posts/{slug}",async(string slug,NpgsqlDataSource db)=>{await using var c=db.CreateCommand("SELECT id,slug,title,body,published_at FROM content.posts WHERE slug=$1 AND status='published'");c.Parameters.AddWithValue(slug);await using var r=await c.ExecuteReaderAsync();return await r.ReadAsync()?Results.Ok(ReadPost(r)):Results.NotFound();}).AllowAnonymous()
.Produces<PostResponse>(200).ProducesProblem(404);
app.MapPost("/internal/admin/posts",async(PostWrite x,HttpRequest req,NpgsqlDataSource db)=>{
  if(!Admin(req,out var user))return Results.NotFound();if(string.IsNullOrWhiteSpace(x.Title)||string.IsNullOrWhiteSpace(x.Slug)||x.Status is not("draft" or "published" or "archived"))return Results.Problem("invalid_post",statusCode:400);var id=Guid.NewGuid();await using var c=db.CreateCommand("INSERT INTO content.posts(id,author_user_id,slug,title,body,status,published_at) VALUES($1,$2,$3,$4,$5,$6,CASE WHEN $6='published' THEN now() END)");c.Parameters.AddWithValue(id);c.Parameters.AddWithValue(user);c.Parameters.AddWithValue(x.Slug.Trim().ToLowerInvariant());c.Parameters.AddWithValue(x.Title.Trim());c.Parameters.AddWithValue(x.Body??"");c.Parameters.AddWithValue(x.Status);try{await c.ExecuteNonQueryAsync();return Results.Created($"/internal/admin/posts/{id}",new CreatedResponse(id));}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.UniqueViolation){return Results.Problem("slug_exists",statusCode:409);}
})
.Produces<CreatedResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
app.MapPut("/internal/admin/posts/{id:guid}",async(Guid id,PostWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out var user))return Results.NotFound();if(string.IsNullOrWhiteSpace(x.Title)||string.IsNullOrWhiteSpace(x.Slug)||x.Status is not("draft" or "published" or "archived"))return Results.Problem("invalid_post",statusCode:400);await using var c=db.CreateCommand("UPDATE content.posts SET slug=$3,title=$4,body=$5,status=$6,published_at=CASE WHEN $6='published' THEN coalesce(published_at,now()) ELSE published_at END,updated_at=now() WHERE id=$1 AND author_user_id=$2");c.Parameters.AddWithValue(id);c.Parameters.AddWithValue(user);c.Parameters.AddWithValue(x.Slug.Trim().ToLowerInvariant());c.Parameters.AddWithValue(x.Title.Trim());c.Parameters.AddWithValue(x.Body??"");c.Parameters.AddWithValue(x.Status);return await c.ExecuteNonQueryAsync()==0?Results.NotFound():Results.NoContent();})
.Produces(204).ProducesProblem(400).ProducesProblem(404);
app.MapDelete("/internal/admin/posts/{id:guid}",async(Guid id,HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out var user))return Results.NotFound();await using var c=db.CreateCommand("UPDATE content.posts SET status='archived',updated_at=now() WHERE id=$1 AND author_user_id=$2");c.Parameters.AddWithValue(id);c.Parameters.AddWithValue(user);return await c.ExecuteNonQueryAsync()==0?Results.NotFound():Results.NoContent();})
.Produces(204).ProducesProblem(404);
app.Run();

static bool Admin(HttpRequest r,out Guid id)=>Guid.TryParse(r.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,out id)&&r.HttpContext.User.IsInRole("admin");
static PostResponse ReadPost(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDateTime(4));
record PostWrite(string Slug,string Title,string? Body,string Status);
record PostResponse(Guid Id,string Slug,string Title,string Body,DateTime PublishedAt);
record CreatedResponse(Guid Id);
record CollectionResponse<T>(List<T> Items);

// ponytail: shared OpenAPI security wiring — bearerAuth for user routes, serviceKey for internal admin/worker/events.
sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        document.Components.SecuritySchemes["serviceKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Service-Key" };
        return Task.CompletedTask;
    }
}
sealed class SecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<AllowAnonymousAttribute>().Any()) return Task.CompletedTask;
        var scheme = metadata.OfType<IAuthorizeData>().Any(data => data.Policy == "serviceKey") ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>() });
        return Task.CompletedTask;
    }
}
