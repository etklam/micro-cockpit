using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Tool") ?? throw new InvalidOperationException("Connection string 'Tool' is required.")));
builder.Services.AddHttpClient("journal",client=>client.BaseAddress=new Uri(builder.Configuration["Services:Journal"]??"http://127.0.0.1:5101"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims=false; o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration"; o.RequireHttpsMetadata=false; o.Audience="trade-diary-services"; });
builder.Services.AddAuthorization(o => { var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build(); o.DefaultPolicy = humanOnly; o.FallbackPolicy = humanOnly; });
builder.Services.AddOpenApi(options=>{options.AddDocumentTransformer<SecuritySchemesTransformer>();options.AddOperationTransformer<SecurityRequirementTransformer>();});
var app=builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();
app.MapGet("/health/ready",async(NpgsqlDataSource db)=>{try{await db.OpenConnectionAsync();return Results.Ok(new{status="ready"});}catch{return Results.Json(new{status="not_ready"},statusCode:503);}}).AllowAnonymous();
app.MapGet("/version",()=>Results.Ok(new{service="tool-service",version="0.1.0"})).AllowAnonymous();
app.MapPost("/internal/tools/position-sizing",(PositionSizing x)=> x.AccountValue<=0||x.RiskPercent<=0||x.RiskPercent>100||x.EntryPrice<=0||x.StopPrice<=0||x.EntryPrice==x.StopPrice
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(PositionSizingResponse.Calculate(x)))
.Produces<PositionSizingResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/risk-reward",(RiskReward x)=> x.EntryPrice<=0||x.StopPrice<=0||x.TargetPrice<=0||!RiskReward.IsValid(x)
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(RiskRewardResponse.Calculate(x)))
.Produces<RiskRewardResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/average-cost",(AverageCost x)=> x.CurrentQuantity<=0||x.CurrentAverageCost<=0||x.AddedQuantity<=0||x.AddedPrice<=0
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(AverageCostResponse.Calculate(x)))
.Produces<AverageCostResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/profit-loss",(ProfitLoss x)=>x.Side is not ("long" or "short")||x.EntryPrice<=0||x.ExitPrice<=0||x.Quantity<=0||x.EntryFee<0||x.ExitFee<0
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(ProfitLossResponse.Calculate(x)))
.Produces<ProfitLossResponse>(200).ProducesProblem(400);
app.MapGet("/internal/tool-presets",async(HttpRequest req,NpgsqlDataSource db)=>ToolStore.TryUser(req,out var user)?Results.Ok(new ToolCollection<PresetResponse>(await ToolStore.Presets(db,user))):Results.Unauthorized());
app.MapPost("/internal/tool-presets",async(PresetWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();if(!ToolValidation.ValidPreset(x))return Results.Problem("invalid_preset",statusCode:400);var item=await ToolStore.CreatePreset(db,user,x);return item is null?Results.Problem("preset_name_exists",statusCode:409):Results.Created($"/internal/tool-presets/{item.Id}",item);});
app.MapPut("/internal/tool-presets/{id:guid}",async(Guid id,PresetWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();if(!ToolValidation.ValidPreset(x))return Results.Problem("invalid_preset",statusCode:400);var count=await ToolStore.UpdatePreset(db,user,id,x);return count switch{-1=>Results.Problem("preset_name_exists",statusCode:409),0=>Results.NotFound(),_=>Results.NoContent()};});
app.MapPost("/internal/tool-presets/{id:guid}/use",async(Guid id,HttpRequest req,NpgsqlDataSource db)=>!ToolStore.TryUser(req,out var user)?Results.Unauthorized():await ToolStore.UsePreset(db,user,id)==0?Results.NotFound():Results.NoContent());
app.MapDelete("/internal/tool-presets/{id:guid}",async(Guid id,HttpRequest req,NpgsqlDataSource db)=>!ToolStore.TryUser(req,out var user)?Results.Unauthorized():await ToolStore.DeletePreset(db,user,id)==0?Results.NotFound():Results.NoContent());
app.MapGet("/internal/saved-calculations",async(HttpRequest req,NpgsqlDataSource db,int limit=10)=>{if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();if(limit is <1 or >50)return Results.Problem("invalid_limit",statusCode:400);return Results.Ok(new ToolCollection<SavedCalculationResponse>(await ToolStore.Recent(db,user,limit)));});
// Persistence model: inputs are schema-v1 snapshots, but output is always recalculated here.
// Frontend-provided result values are never accepted as authoritative.
app.MapPost("/internal/saved-calculations",async(SavedCalculationWrite x,HttpRequest req,NpgsqlDataSource db,IHttpClientFactory clients)=>{if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();var key=req.Headers["Idempotency-Key"].FirstOrDefault();if(key is null||key.Length is <8 or >100||!ToolValidation.ValidCurrency(x.Currency)||!ToolValidation.ValidSymbol(x.Symbol)||x.Note?.Length>1000||x.SourceTransactionId is not null&&x.SourceDiaryId is null||!ToolValidation.TryCalculate(x.ToolType,x.Inputs,out var output))return Results.Problem("invalid_calculation",statusCode:400);if(x.SourceDiaryId is not null&&!await SourceReferenceValidator.Owns(clients.CreateClient("journal"),req,x.SourceDiaryId.Value,x.SourceTransactionId))return Results.Problem("source_not_found",statusCode:404);var saved=await ToolStore.Save(db,user,x,output!,key);return saved.Duplicate?Results.Ok(saved.Item):Results.Created($"/internal/saved-calculations/{saved.Item!.Id}",saved.Item);});
app.MapDelete("/internal/saved-calculations/{id:guid}",async(Guid id,HttpRequest req,NpgsqlDataSource db)=>!ToolStore.TryUser(req,out var user)?Results.Unauthorized():await ToolStore.DeleteSaved(db,user,id)==0?Results.NotFound():Results.NoContent());
app.Run();

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
