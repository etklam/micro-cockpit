using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.MetadataAddress = builder.Configuration["Auth:MetadataAddress"] ?? "http://127.0.0.1:5100/.well-known/openid-configuration";
    options.RequireHttpsMetadata = false;
    options.Audience = "trade-diary-services";
});
builder.Services.AddAuthorization(EdgeAuthorization.Configure);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ProxyHeaders.ConfigureForwardedHeaders(options, builder.Configuration));
builder.Services.AddRateLimiter(AuthRateLimiting.Configure);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EdgeTransport>();

foreach (var (service, fallback) in EdgeServices.All)
{
    var key = string.Concat(service.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    builder.Services.AddHttpClient(service, client =>
    {
        client.BaseAddress = new Uri(builder.Configuration[$"Services:{key}"] ?? fallback);
        client.Timeout = Timeout.InfiniteTimeSpan;
    });
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<EdgeExceptionMiddleware>();
app.UseMiddleware<CorrelationMiddleware>();
app.UseStatusCodePages(EdgeProblems.WriteStatusCodeAsync);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// After endpoint selection, before Minimal API body binding / Identity proxy.
app.UseMiddleware<AuthBodyLimitMiddleware>();

HealthEndpoints.Map(app);
AuthenticationEndpoints.Map(app);
BootstrapEndpoints.Map(app);
SettingsEndpoints.Map(app);
DashboardEndpoints.Map(app);
CalendarEndpoints.Map(app);
JournalEndpoints.Map(app);
PerformanceEndpoints.Map(app);
DisciplineEndpoints.Map(app);
ReminderEndpoints.Map(app);
ResearchEndpoints.Map(app);
AdminEndpoints.Map(app);
PartnerEndpoints.Map(app);
CompositionEndpoints.Map(app);

app.Run();

public partial class Program;
