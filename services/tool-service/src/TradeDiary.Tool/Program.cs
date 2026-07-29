using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Npgsql;
using TradeDiary.Authorization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_=>NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Tool") ?? throw new InvalidOperationException("Connection string 'Tool' is required.")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims=false; o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration"; o.RequireHttpsMetadata=false; o.Audience="trade-diary-services"; });
builder.Services.AddAuthorization(TradeDiaryPolicies.Configure);
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
app.MapPost("/internal/saved-calculations",async(SavedCalculationWrite x,HttpRequest req,NpgsqlDataSource db)=>{if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();var key=req.Headers["Idempotency-Key"].FirstOrDefault();if(key is null||key.Length is <8 or >100||!ToolValidation.ValidCurrency(x.Currency)||!ToolValidation.ValidSymbol(x.Symbol)||x.Note?.Length>1000||!ToolValidation.TryCalculate(x.ToolType,x.Inputs,out var output))return Results.Problem("invalid_calculation",statusCode:400);var saved=await ToolStore.Save(db,user,x,output!,key);return saved.Duplicate?Results.Ok(saved.Item):Results.Created($"/internal/saved-calculations/{saved.Item!.Id}",saved.Item);});
app.MapDelete("/internal/saved-calculations/{id:guid}",async(Guid id,HttpRequest req,NpgsqlDataSource db)=>!ToolStore.TryUser(req,out var user)?Results.Unauthorized():await ToolStore.DeleteSaved(db,user,id)==0?Results.NotFound():Results.NoContent());
app.MapGet("/internal/account-export",async(HttpRequest req,NpgsqlDataSource db)=>{
  if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();
  async Task<JsonElement> Rows(string sql){await using var command=db.CreateCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb) FROM ({sql}) row_data");command.Parameters.AddWithValue(user);return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!).RootElement.Clone();}
  return Results.Ok(new ToolAccountExport(
    await Rows("SELECT * FROM tool.presets WHERE user_id=$1 ORDER BY created_at,id"),
    await Rows("SELECT * FROM tool.saved_calculations WHERE user_id=$1 ORDER BY created_at,id")));
}).Produces<ToolAccountExport>(200).ProducesProblem(401);
app.MapDelete("/internal/account-data",async(HttpRequest req,NpgsqlDataSource db)=>{
  if(!ToolStore.TryUser(req,out var user))return Results.Unauthorized();
  await using var connection=await db.OpenConnectionAsync();await using var tx=await connection.BeginTransactionAsync();
  foreach(var sql in new[]{"DELETE FROM tool.saved_calculations WHERE user_id=$1","DELETE FROM tool.presets WHERE user_id=$1"}){await using var command=new NpgsqlCommand(sql,connection,tx);command.Parameters.AddWithValue(user);await command.ExecuteNonQueryAsync();}
  await tx.CommitAsync();return Results.NoContent();
}).Produces(204).ProducesProblem(401);
app.Run();

record ToolAccountExport(JsonElement Presets,JsonElement SavedCalculations);

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
