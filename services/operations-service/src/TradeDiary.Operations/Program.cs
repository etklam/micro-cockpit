using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Operations") ?? throw new InvalidOperationException("Connection string 'Operations' is required.")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{o.MapInboundClaims=false;o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration";o.RequireHttpsMetadata=false;o.Audience="trade-diary-services";});
builder.Services.AddSingleton<IAuthorizationHandler, ServiceKeyAuthorizationHandler>();
builder.Services.AddAuthorization(o=>
{
    var humanOnly=new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build();
    o.DefaultPolicy=humanOnly;o.FallbackPolicy=humanOnly;
    o.AddPolicy("serviceKey", policy => policy.AddRequirements(new ServiceKeyRequirement()));
});
builder.Services.AddOpenApi(options=>{options.AddDocumentTransformer<SecuritySchemesTransformer>();options.AddOperationTransformer<SecurityRequirementTransformer>();});
var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();app.MapGet("/health/ready",async(NpgsqlDataSource db)=>{try{await db.OpenConnectionAsync();return Results.Ok(new{status="ready"});}catch{return Results.Json(new{status="not_ready"},statusCode:503);}}).AllowAnonymous();app.MapGet("/version",()=>Results.Ok(new{service="operations-service",version="0.1.0"})).AllowAnonymous();
app.MapGet("/internal/operations/audit",async(int? limit,HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out _))return Results.NotFound();await using var c=db.CreateCommand("SELECT id,actor_user_id,action,resource_type,resource_id,details::text,occurred_at FROM operations.audit_events ORDER BY occurred_at DESC LIMIT $1");c.Parameters.AddWithValue(Math.Clamp(limit??100,1,500));await using var r=await c.ExecuteReaderAsync();var items=new List<AuditResponse>();while(await r.ReadAsync())items.Add(new AuditResponse(r.GetGuid(0),r.IsDBNull(1)?null:(Guid?)r.GetGuid(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),JsonSerializer.Deserialize<JsonElement>(r.GetString(5)),r.GetDateTime(6)));return Results.Ok(new CollectionResponse<AuditResponse>(items));})
.Produces<CollectionResponse<AuditResponse>>(200).ProducesProblem(404);
app.MapPost("/internal/operations/audit",async(AuditWrite x,NpgsqlDataSource db)=>{await using var c=db.CreateCommand("INSERT INTO operations.audit_events(id,actor_user_id,action,resource_type,resource_id,details) VALUES($1,NULL,$2,$3,$4,$5::jsonb)");c.Parameters.AddWithValue(Guid.NewGuid());c.Parameters.AddWithValue(x.Action);c.Parameters.AddWithValue(x.ResourceType);c.Parameters.AddWithValue((object?)x.ResourceId??DBNull.Value);c.Parameters.AddWithValue(JsonSerializer.Serialize(x.Details));await c.ExecuteNonQueryAsync();return Results.NoContent();})
.RequireAuthorization("serviceKey")
.Produces(204).ProducesProblem(401);
app.MapPost("/internal/operations/jobs",async(JobWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out var user))return Results.NotFound();var id=Guid.NewGuid();await using var c=db.CreateCommand("INSERT INTO operations.job_registry(id,job_type,status,requested_by,payload) VALUES($1,$2,'queued',$3,$4::jsonb)");c.Parameters.AddWithValue(id);c.Parameters.AddWithValue(x.JobType);c.Parameters.AddWithValue(user);c.Parameters.AddWithValue(JsonSerializer.Serialize(x.Payload));await c.ExecuteNonQueryAsync();return Results.Accepted($"/internal/operations/jobs/{id}",new JobAcceptedResponse(id,"queued"));})
.Produces<JobAcceptedResponse>(202).ProducesProblem(404);
app.MapGet("/internal/operations/jobs",async(HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out _))return Results.NotFound();await using var c=db.CreateCommand("SELECT id,job_type,status,requested_by,created_at,updated_at FROM operations.job_registry ORDER BY created_at DESC LIMIT 100");await using var r=await c.ExecuteReaderAsync();var items=new List<JobResponse>();while(await r.ReadAsync())items.Add(new JobResponse(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetGuid(3),r.GetDateTime(4),r.GetDateTime(5)));return Results.Ok(new CollectionResponse<JobResponse>(items));})
.Produces<CollectionResponse<JobResponse>>(200).ProducesProblem(404);
app.MapPost("/internal/operations/health",async(HealthWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!Admin(req,out _))return Results.NotFound();await using var c=db.CreateCommand("INSERT INTO operations.service_health_history(id,service_name,status) VALUES($1,$2,$3)");c.Parameters.AddWithValue(Guid.NewGuid());c.Parameters.AddWithValue(x.ServiceName);c.Parameters.AddWithValue(x.Status);await c.ExecuteNonQueryAsync();return Results.NoContent();})
.Produces(204).ProducesProblem(404);
app.Run();

static bool Admin(HttpRequest r,out Guid id)=>Guid.TryParse(r.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,out id)&&r.HttpContext.User.IsInRole("admin");
record AuditWrite(string Action,string ResourceType,string? ResourceId,JsonElement Details);record JobWrite(string JobType,JsonElement Payload);record HealthWrite(string ServiceName,string Status);
record AuditResponse(Guid Id,Guid? ActorUserId,string Action,string ResourceType,string? ResourceId,JsonElement Details,DateTime OccurredAt);
record JobAcceptedResponse(Guid Id,string Status);
record JobResponse(Guid Id,string JobType,string Status,Guid RequestedBy,DateTime CreatedAt,DateTime UpdatedAt);
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

sealed class ServiceKeyRequirement : IAuthorizationRequirement { }
sealed class ServiceKeyAuthorizationHandler(IConfiguration configuration) : AuthorizationHandler<ServiceKeyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ServiceKeyRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && ServiceKeyAuthorization.IsValid(httpContext.Request.Headers["X-Service-Key"].FirstOrDefault(), configuration["Internal:ServiceKey"] ?? ""))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
public static class ServiceKeyAuthorization
{
    public static bool IsValid(string? supplied, string expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected)) return false;
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
