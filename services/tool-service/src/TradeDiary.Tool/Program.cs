using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims=false; o.MetadataAddress=builder.Configuration["Auth:MetadataAddress"]??"http://127.0.0.1:5100/.well-known/openid-configuration"; o.RequireHttpsMetadata=false; o.Audience="trade-diary-services"; });
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var app=builder.Build(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live",()=>Results.Ok(new{status="healthy"})).AllowAnonymous();
app.MapGet("/health/ready",()=>Results.Ok(new{status="ready"})).AllowAnonymous();
app.MapGet("/version",()=>Results.Ok(new{service="tool-service",version="0.1.0"})).AllowAnonymous();
app.MapPost("/internal/tools/position-sizing",(PositionSizing x)=> x.AccountValue<=0||x.RiskPercent<=0||x.EntryPrice<=0||x.StopPrice<0||x.EntryPrice==x.StopPrice
  ? Results.BadRequest(new{error="invalid_input"})
  : Results.Ok(new{riskAmount=x.AccountValue*x.RiskPercent/100m,quantity=decimal.Floor((x.AccountValue*x.RiskPercent/100m)/Math.Abs(x.EntryPrice-x.StopPrice)),perUnitRisk=Math.Abs(x.EntryPrice-x.StopPrice)}));
app.MapPost("/internal/tools/risk-reward",(RiskReward x)=> x.EntryPrice<=0||x.StopPrice<0||x.TargetPrice<=0||x.EntryPrice==x.StopPrice
  ? Results.BadRequest(new{error="invalid_input"})
  : Results.Ok(new{risk=Math.Abs(x.EntryPrice-x.StopPrice),reward=Math.Abs(x.TargetPrice-x.EntryPrice),ratio=decimal.Round(Math.Abs(x.TargetPrice-x.EntryPrice)/Math.Abs(x.EntryPrice-x.StopPrice),4)}));
app.MapPost("/internal/tools/fire",(Fire x)=> x.AnnualExpenses<=0||x.WithdrawalRatePercent<=0
  ? Results.BadRequest(new{error="invalid_input"})
  : Results.Ok(new{target=decimal.Round(x.AnnualExpenses/(x.WithdrawalRatePercent/100m),2),gap=decimal.Max(0,decimal.Round(x.AnnualExpenses/(x.WithdrawalRatePercent/100m)-x.InvestedAssets,2))}));
app.Run();
record PositionSizing(decimal AccountValue,decimal RiskPercent,decimal EntryPrice,decimal StopPrice);
record RiskReward(decimal EntryPrice,decimal StopPrice,decimal TargetPrice);
record Fire(decimal AnnualExpenses,decimal WithdrawalRatePercent,decimal InvestedAssets);
