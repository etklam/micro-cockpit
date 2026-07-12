using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims=false; o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration"; o.RequireHttpsMetadata=false; o.Audience="trade-diary-services"; });
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddOpenApi(options=>{options.AddDocumentTransformer<SecuritySchemesTransformer>();options.AddOperationTransformer<SecurityRequirementTransformer>();});
var app=builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapOpenApi("/openapi.json").AllowAnonymous();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();
app.MapGet("/health/ready",()=>Results.Ok(new{status="ready"})).AllowAnonymous();
app.MapGet("/version",()=>Results.Ok(new{service="tool-service",version="0.1.0"})).AllowAnonymous();
app.MapPost("/internal/tools/position-sizing",(PositionSizing x)=> x.AccountValue<=0||x.RiskPercent<=0||x.EntryPrice<=0||x.StopPrice<0||x.EntryPrice==x.StopPrice
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(new PositionSizingResponse(x.AccountValue*x.RiskPercent/100m,decimal.Floor((x.AccountValue*x.RiskPercent/100m)/Math.Abs(x.EntryPrice-x.StopPrice)),Math.Abs(x.EntryPrice-x.StopPrice))))
.Produces<PositionSizingResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/risk-reward",(RiskReward x)=> x.EntryPrice<=0||x.StopPrice<0||x.TargetPrice<=0||x.EntryPrice==x.StopPrice
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(new RiskRewardResponse(Math.Abs(x.EntryPrice-x.StopPrice),Math.Abs(x.TargetPrice-x.EntryPrice),decimal.Round(Math.Abs(x.TargetPrice-x.EntryPrice)/Math.Abs(x.EntryPrice-x.StopPrice),4))))
.Produces<RiskRewardResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/fire",(Fire x)=> x.AnnualExpenses<=0||x.WithdrawalRatePercent<=0
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(new FireResponse(decimal.Round(x.AnnualExpenses/(x.WithdrawalRatePercent/100m),2),decimal.Max(0,decimal.Round(x.AnnualExpenses/(x.WithdrawalRatePercent/100m)-x.InvestedAssets,2)))))
.Produces<FireResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/relative-value",(RelativeValue x)=>x.AssetPrice<=0||x.BenchmarkPrice<=0||x.HistoricalRatio<=0
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(new RelativeValueResponse(decimal.Round(x.AssetPrice/x.BenchmarkPrice,6),decimal.Round(((x.AssetPrice/x.BenchmarkPrice)/x.HistoricalRatio-1)*100m,4))))
.Produces<RelativeValueResponse>(200).ProducesProblem(400);
app.MapPost("/internal/tools/seasonality",(Seasonality x)=>x.Returns is null||x.Returns.Count==0||x.Returns.Count>120
  ? Results.Problem("invalid_input",statusCode:400)
  : Results.Ok(new SeasonalityResponse(x.Returns.Count,decimal.Round(x.Returns.Average(),4),decimal.Round((decimal)x.Returns.Count(v=>v>0)/x.Returns.Count*100m,2))))
.Produces<SeasonalityResponse>(200).ProducesProblem(400);
app.Run();

record PositionSizing(decimal AccountValue,decimal RiskPercent,decimal EntryPrice,decimal StopPrice);
record RiskReward(decimal EntryPrice,decimal StopPrice,decimal TargetPrice);
record Fire(decimal AnnualExpenses,decimal WithdrawalRatePercent,decimal InvestedAssets);
record RelativeValue(decimal AssetPrice,decimal BenchmarkPrice,decimal HistoricalRatio);
record Seasonality(List<decimal> Returns);
record PositionSizingResponse(decimal RiskAmount,decimal Quantity,decimal PerUnitRisk);
record RiskRewardResponse(decimal Risk,decimal Reward,decimal Ratio);
record FireResponse(decimal Target,decimal Gap);
record RelativeValueResponse(decimal CurrentRatio,decimal DeviationPercent);
record SeasonalityResponse(int Observations,decimal AverageReturn,decimal PositiveRate);

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
        var path = "/" + (context.Description.RelativePath ?? string.Empty);
        var scheme = path.Contains("/internal/admin/", StringComparison.Ordinal) || path.Contains("/internal/worker/", StringComparison.Ordinal) || path.Contains("/internal/events/", StringComparison.Ordinal) ? "serviceKey" : "bearerAuth";
        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(scheme, context.Document)] = new List<string>() });
        return Task.CompletedTask;
    }
}
