using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims=false; o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration"; o.RequireHttpsMetadata=false; o.Audience="trade-diary-services"; });
builder.Services.AddAuthorization(o => { var humanOnly = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => context.User.FindFirst("account_type")?.Value != "agent").Build(); o.DefaultPolicy = humanOnly; o.FallbackPolicy = humanOnly; });
builder.Services.AddOpenApi(options=>{options.AddDocumentTransformer<SecuritySchemesTransformer>();options.AddOperationTransformer<SecurityRequirementTransformer>();});
var app=builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();
app.MapGet("/health/ready",()=>Results.Ok(new{status="ready"})).AllowAnonymous();
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
app.Run();

record PositionSizing(decimal AccountValue,decimal RiskPercent,decimal EntryPrice,decimal StopPrice);
record RiskReward(decimal EntryPrice,decimal StopPrice,decimal TargetPrice)
{
    public static bool IsValid(RiskReward x) =>
        x.StopPrice < x.EntryPrice && x.TargetPrice > x.EntryPrice ||
        x.StopPrice > x.EntryPrice && x.TargetPrice < x.EntryPrice;
}
record AverageCost(decimal CurrentQuantity,decimal CurrentAverageCost,decimal AddedQuantity,decimal AddedPrice);
record ProfitLoss(string Side,decimal EntryPrice,decimal ExitPrice,decimal Quantity,decimal EntryFee,decimal ExitFee);
record PositionSizingResponse(decimal Quantity,decimal PlannedLoss,decimal RiskBudget,decimal PositionValue,decimal PerUnitRisk)
{
    public static PositionSizingResponse Calculate(PositionSizing x)
    {
        var riskBudget=x.AccountValue*x.RiskPercent/100m;
        var perUnitRisk=Math.Abs(x.EntryPrice-x.StopPrice);
        var quantity=decimal.Floor(riskBudget/perUnitRisk);
        return new(quantity,decimal.Round(quantity*perUnitRisk,2),decimal.Round(riskBudget,2),decimal.Round(quantity*x.EntryPrice,2),decimal.Round(perUnitRisk,6));
    }
}
record RiskRewardResponse(decimal Ratio,decimal RiskPerUnit,decimal RewardPerUnit,decimal BreakevenWinRate)
{
    public static RiskRewardResponse Calculate(RiskReward x)
    {
        var risk=Math.Abs(x.EntryPrice-x.StopPrice);
        var reward=Math.Abs(x.TargetPrice-x.EntryPrice);
        var ratio=reward/risk;
        return new(decimal.Round(ratio,4),decimal.Round(risk,6),decimal.Round(reward,6),decimal.Round(100m/(1m+ratio),2));
    }
}
record AverageCostResponse(decimal AverageCost,decimal TotalQuantity,decimal TotalCost,decimal AverageCostChange)
{
    public static AverageCostResponse Calculate(AverageCost x)
    {
        var totalQuantity=x.CurrentQuantity+x.AddedQuantity;
        var totalCost=x.CurrentQuantity*x.CurrentAverageCost+x.AddedQuantity*x.AddedPrice;
        var average=totalCost/totalQuantity;
        return new(decimal.Round(average,6),decimal.Round(totalQuantity,6),decimal.Round(totalCost,2),decimal.Round((average/x.CurrentAverageCost-1m)*100m,2));
    }
}
record ProfitLossResponse(decimal NetPnl,decimal ReturnPercent,decimal GrossPnl,decimal TotalFees,decimal ExitValue)
{
    public static ProfitLossResponse Calculate(ProfitLoss x)
    {
        var direction=x.Side=="short"?-1m:1m;
        var gross=(x.ExitPrice-x.EntryPrice)*x.Quantity*direction;
        var fees=x.EntryFee+x.ExitFee;
        var net=gross-fees;
        return new(decimal.Round(net,2),decimal.Round(net/(x.EntryPrice*x.Quantity+x.EntryFee)*100m,2),decimal.Round(gross,2),decimal.Round(fees,2),decimal.Round(x.ExitPrice*x.Quantity,2));
    }
}

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
