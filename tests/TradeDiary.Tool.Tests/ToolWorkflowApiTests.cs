using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class ToolWorkflowApiTests
{
    [Fact]
    public async Task Presets_and_saved_calculations_are_validated_recalculated_idempotent_and_user_scoped()
    {
        await using var postgres=new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();await postgres.StartAsync();
        await using var setup=new NpgsqlConnection(postgres.GetConnectionString());await setup.OpenAsync();
        await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(FindRoot(),"platform/postgres/migrations/0024_tool_workflows.sql")),setup).ExecuteNonQueryAsync();
        await using var dataSource=NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>{services.RemoveAll<NpgsqlDataSource>();services.AddSingleton(dataSource);services.AddHttpClient("journal").ConfigurePrimaryHttpMessageHandler(()=>new SourceHandler());services.AddAuthentication(options=>{options.DefaultAuthenticateScheme=TestAuth.Scheme;options.DefaultChallengeScheme=TestAuth.Scheme;}).AddScheme<AuthenticationSchemeOptions,TestAuth>(TestAuth.Scheme,_=>{});}));
        var owner=Guid.NewGuid();var other=Guid.NewGuid();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Test-User",owner.ToString());
        var preset=new{name="Core risk",toolType="position-sizing",inputs=new{accountValue=10000,riskPercent=1},currency="USD"};
        using var created=await client.PostAsJsonAsync("/internal/tool-presets",preset);Assert.Equal(HttpStatusCode.Created,created.StatusCode);var presetId=JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/internal/tool-presets",preset)).StatusCode);
        using var bad=await client.PostAsJsonAsync("/internal/tool-presets",new{name="Bad",toolType="position-sizing",inputs=new{riskPercent=101},currency="USD"});Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync($"/internal/tool-presets/{presetId}",new{name="Core risk updated",toolType="position-sizing",inputs=new{accountValue=20000,riskPercent=2},currency="USD"})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsync($"/internal/tool-presets/{presetId}/use",null)).StatusCode);
        var listed=(await client.GetFromJsonAsync<JsonElement>("/internal/tool-presets")).GetProperty("items").EnumerateArray().Single();Assert.Equal("Core risk updated",listed.GetProperty("name").GetString());Assert.NotEqual(JsonValueKind.Null,listed.GetProperty("lastUsedAt").ValueKind);
        client.DefaultRequestHeaders.Authorization=new("Bearer","owner-source");
        using var rejectedSource=new HttpRequestMessage(HttpMethod.Post,"/internal/saved-calculations"){Content=JsonContent.Create(new{toolType="position-sizing",inputs=new{accountValue=10000,riskPercent=1,entryPrice=100,stopPrice=95},currency="USD",sourceDiaryId=Guid.NewGuid(),sourceTransactionId=Guid.NewGuid()})};rejectedSource.Headers.Add("Idempotency-Key","bad-source-key");Assert.Equal(HttpStatusCode.NotFound,(await client.SendAsync(rejectedSource)).StatusCode);
        using var saveRequest=new HttpRequestMessage(HttpMethod.Post,"/internal/saved-calculations"){Content=JsonContent.Create(new{toolType="position-sizing",inputs=new{accountValue=10000,riskPercent=1,entryPrice=100,stopPrice=95},currency="USD",symbol="AAPL",note="Plan"})};saveRequest.Headers.Add("Idempotency-Key","same-save-key");
        using var saved=await client.SendAsync(saveRequest);Assert.Equal(HttpStatusCode.Created,saved.StatusCode);var savedDoc=JsonDocument.Parse(await saved.Content.ReadAsStringAsync());Assert.Equal(20,savedDoc.RootElement.GetProperty("output").GetProperty("quantity").GetDecimal());var savedId=savedDoc.RootElement.GetProperty("id").GetGuid();
        using var duplicateRequest=new HttpRequestMessage(HttpMethod.Post,"/internal/saved-calculations"){Content=JsonContent.Create(new{toolType="position-sizing",inputs=new{accountValue=10000,riskPercent=1,entryPrice=100,stopPrice=95},currency="USD",symbol="AAPL",note="Plan"})};duplicateRequest.Headers.Add("Idempotency-Key","same-save-key");Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(duplicateRequest)).StatusCode);
        using var otherClient=factory.CreateClient();otherClient.DefaultRequestHeaders.Add("X-Test-User",other.ToString());
        Assert.Empty((await otherClient.GetFromJsonAsync<JsonElement>("/internal/tool-presets")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await otherClient.DeleteAsync($"/internal/tool-presets/{presetId}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await otherClient.DeleteAsync($"/internal/saved-calculations/{savedId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync($"/internal/saved-calculations/{savedId}")).StatusCode);
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder){internal new const string Scheme="tool-test";protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var id=Request.Headers["X-Test-User"].FirstOrDefault();if(id is null)return Task.FromResult(AuthenticateResult.NoResult());var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub",id),new Claim("account_type","human")],Scheme));return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme)));}}
    private sealed class SourceHandler:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(request.Headers.Authorization?.Parameter=="allowed-source"?HttpStatusCode.OK:HttpStatusCode.NotFound));}
    private static string FindRoot(){var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"TradeDiary.slnx")))directory=directory.Parent;return directory?.FullName??throw new InvalidOperationException();}
}
