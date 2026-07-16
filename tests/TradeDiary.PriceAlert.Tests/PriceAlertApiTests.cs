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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class PriceAlertApiTests
{
    [Fact]
    public async Task Api_defaults_to_close_and_preserves_user_ownership_for_lifecycle_actions()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        await using var setup = new NpgsqlConnection(postgres.GetConnectionString());
        await setup.OpenAsync();
        var root = FindRoot();
        foreach (var id in new[] { "0007_market_data.sql", "0009_price_alert.sql", "0016_market_daily_bar_price_contract.sql", "0017_price_alert_evaluation_price.sql" })
            await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", id)), setup).ExecuteNonQueryAsync();
        var runId = Guid.NewGuid();
        await new NpgsqlCommand($"INSERT INTO market.symbols(symbol,name,exchange,currency,timezone) VALUES ('TEST','Test','NYSE','USD','America/New_York'); INSERT INTO market.provider_runs(id,provider,started_at,completed_at,status) VALUES ('{runId}','test',now(),now(),'succeeded'); INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id,published_at) VALUES ('TEST','2026-07-16',100,110,90,105,1000,'test','{runId}',now())", setup).ExecuteNonQueryAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>(); services.RemoveAll<IHostedService>(); services.AddSingleton(dataSource);
            services.AddAuthentication(options => { options.DefaultAuthenticateScheme = TestAuth.Scheme; options.DefaultChallengeScheme = TestAuth.Scheme; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
        }));
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        using var ownerClient = factory.CreateClient(); ownerClient.DefaultRequestHeaders.Add("X-Test-User", owner.ToString());
        using var create = await ownerClient.PostAsJsonAsync("/internal/price-alerts", new { symbol = "TEST", conditionType = "above", threshold = 110, lookbackDays = (int?)null, direction = (string?)null });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var document = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal("close", document.RootElement.GetProperty("evaluationPrice").GetString());
        var alertId = document.RootElement.GetProperty("id").GetGuid();

        await new NpgsqlCommand($"""
            UPDATE price_alert.alerts SET status='triggered',last_evaluated_date='2026-07-16' WHERE id='{alertId}';
            INSERT INTO price_alert.triggers(id,alert_id,trading_date,observed_close,observed_price,price_type,triggered_at)
            VALUES ('{Guid.NewGuid()}','{alertId}','2026-07-14',100,100,'close','2026-07-14T22:00:00Z'),
                   ('{Guid.NewGuid()}','{alertId}','2026-07-15',105,105,'close','2026-07-15T22:00:00Z');
            """, setup).ExecuteNonQueryAsync();
        using var otherClient = factory.CreateClient(); otherClient.DefaultRequestHeaders.Add("X-Test-User", other.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/internal/price-alerts/{alertId}/triggers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync($"/internal/price-alerts/{alertId}/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync($"/internal/price-alerts/{alertId}/reactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/internal/price-alerts/{alertId}/dismiss", null)).StatusCode);
        await using (var dismissed = new NpgsqlCommand("SELECT status,(SELECT count(*) FROM price_alert.triggers WHERE alert_id=$1 AND dismissed_at IS NOT NULL),(SELECT dismissed_at IS NULL FROM price_alert.triggers WHERE alert_id=$1 ORDER BY triggered_at LIMIT 1) FROM price_alert.alerts WHERE id=$1", setup))
        {
            dismissed.Parameters.AddWithValue(alertId);
            await using var reader = await dismissed.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal("dismissed", reader.GetString(0)); Assert.Equal(1L, reader.GetInt64(1)); Assert.True(reader.GetBoolean(2));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/internal/price-alerts/{alertId}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/internal/price-alerts/{alertId}/dismiss", null)).StatusCode);
        Assert.Equal(1L, (long)(await new NpgsqlCommand($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}' AND dismissed_at IS NOT NULL", setup).ExecuteScalarAsync())!);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/internal/price-alerts/{alertId}/reactivate", null)).StatusCode);
        await using (var verify = new NpgsqlCommand("SELECT status,last_evaluated_date FROM price_alert.alerts WHERE id=$1", setup))
        {
            verify.Parameters.AddWithValue(alertId);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal("active", reader.GetString(0)); Assert.Equal(new DateOnly(2026, 7, 16), reader.GetFieldValue<DateOnly>(1));
        }
        Assert.Equal(0, await PriceAlertEngine.Evaluate(dataSource, 100));
        Assert.Equal(2L, (long)(await new NpgsqlCommand($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}'", setup).ExecuteScalarAsync())!);
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal new const string Scheme = "price-alert-test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = Request.Headers["X-Test-User"].FirstOrDefault();
            if (id is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id), new Claim("account_type", "human")], Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TradeDiary.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
