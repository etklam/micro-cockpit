using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class InstrumentDirectoryApiTests
{
    [Fact]
    public async Task Directory_keeps_stable_instrument_identity_when_symbol_changes()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("test").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        await using (var setup = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await setup.OpenAsync();
            var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
            foreach (var file in new[] { "0007_market_data.sql", "0027_market_instrument_identity.sql", "0034_market_daily_close_adjustments.sql" })
                await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(root, "platform/postgres/migrations", file)), setup).ExecuteNonQueryAsync();
        }

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Internal:ServiceKey"] = "test-key" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(dataSource);
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Key", "test-key");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/internal/admin/symbols/AAPL", new
        {
            name = "Apple Inc.", exchange = "NASDAQ", currency = "USD", timezone = "America/New_York", active = true,
        })).StatusCode);
        var first = await client.GetFromJsonAsync<JsonElement>("/internal/v1/symbols");
        var instrumentId = first.GetProperty("items")[0].GetProperty("instrumentId").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/internal/admin/symbols/AAPX", new
        {
            instrumentId, name = "Apple Inc.", exchange = "NASDAQ", currency = "USD", timezone = "America/New_York", active = true,
        })).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/internal/v1/symbols");
        Assert.Equal(1, after.GetProperty("items").GetArrayLength());
        Assert.Equal(instrumentId, after.GetProperty("items")[0].GetProperty("instrumentId").GetGuid());
        Assert.Equal("AAPX", after.GetProperty("items")[0].GetProperty("symbol").GetString());

        await using (var history = dataSource.CreateCommand("SELECT symbol,valid_to IS NULL FROM market.instrument_symbol_history WHERE instrument_id=$1 ORDER BY valid_from"))
        {
            history.Parameters.AddWithValue(instrumentId);
            await using var reader = await history.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal("AAPL", reader.GetString(0)); Assert.False(reader.GetBoolean(1));
            Assert.True(await reader.ReadAsync()); Assert.Equal("AAPX", reader.GetString(0)); Assert.True(reader.GetBoolean(1));
            Assert.False(await reader.ReadAsync());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/internal/admin/symbols/OLD", new
        {
            instrumentId, name = "Old alias", exchange = "NASDAQ", currency = "USD", timezone = "America/New_York", active = false,
        })).StatusCode);
        var withInactiveAlias = await client.GetFromJsonAsync<JsonElement>("/internal/v1/symbols");
        Assert.Equal("AAPX", withInactiveAlias.GetProperty("items")[0].GetProperty("symbol").GetString());

        var run = await (await client.PostAsJsonAsync("/internal/admin/provider-runs", new { provider = "external-test" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var runId = run.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/internal/admin/provider-runs/{runId}/bars", new[]
        {
            new { symbol = "AAPX", tradingDate = "2026-07-28", open = 98m, high = 102m, low = 97m, close = 100m, adjustedClose = 50m, volume = 1_000_000m },
        })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/internal/v1/bars/AAPX?from=2026-07-28&to=2026-07-28")).GetProperty("items").EnumerateArray());
        Assert.Equal("unavailable", (await client.GetFromJsonAsync<JsonElement>($"/internal/v1/instruments/{instrumentId}/daily-close?onOrBefore=2026-07-28")).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/internal/admin/provider-runs/{runId}/complete", new { status = "succeeded", error = (string?)null })).StatusCode);
        var bars = await client.GetFromJsonAsync<JsonElement>("/internal/v1/bars/AAPX?from=2026-07-28&to=2026-07-28");
        Assert.Equal(100m, bars.GetProperty("items")[0].GetProperty("rawClose").GetDecimal());
        Assert.Equal(50m, bars.GetProperty("items")[0].GetProperty("adjustedClose").GetDecimal());
        var dailyClose = await client.GetFromJsonAsync<JsonElement>($"/internal/v1/instruments/{instrumentId}/daily-close?onOrBefore=2026-07-28");
        Assert.Equal("available", dailyClose.GetProperty("status").GetString());
        Assert.Equal(100m, dailyClose.GetProperty("rawClose").GetDecimal());
        Assert.Equal(50m, dailyClose.GetProperty("adjustedClose").GetDecimal());

        var failedRun = await (await client.PostAsJsonAsync("/internal/admin/provider-runs", new { provider = "external-test" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var failedRunId = failedRun.GetProperty("id").GetGuid();
        await client.PutAsJsonAsync($"/internal/admin/provider-runs/{failedRunId}/bars", new[]
        {
            new { symbol = "AAPX", tradingDate = "2026-07-28", open = 198m, high = 202m, low = 197m, close = 200m, adjustedClose = 100m, volume = 1_000_000m },
        });
        await client.PostAsJsonAsync($"/internal/admin/provider-runs/{failedRunId}/complete", new { status = "failed", error = "provider timeout" });
        var afterFailure = await client.GetFromJsonAsync<JsonElement>("/internal/v1/bars/AAPX?from=2026-07-28&to=2026-07-28");
        Assert.Equal(100m, afterFailure.GetProperty("items")[0].GetProperty("rawClose").GetDecimal());
        Assert.Equal("v1", (await client.GetFromJsonAsync<JsonElement>("/version")).GetProperty("contract").GetString());
    }
}
