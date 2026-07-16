using Npgsql;
using Testcontainers.PostgreSql;

[CollectionDefinition("price-alert-worker", DisableParallelization = true)]
public sealed class PriceAlertWorkerCollection;

[Collection("price-alert-worker")]
public sealed class PriceAlertWorkerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine")
        .WithDatabase("trade_diary").WithUsername("trade_diary").WithPassword("test-only-container-password").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (var migration in Directory.GetFiles(FindRoot(), "*.sql").Order(StringComparer.Ordinal))
            await new NpgsqlCommand(await File.ReadAllTextAsync(migration), connection).ExecuteNonQueryAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Open_alert_records_the_published_open_price()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "open");
        await SeedBarAsync(new DateOnly(2026, 7, 16), 110m, 120m, 80m, 90m);

        Assert.Equal(1, await PriceAlertEngine.Evaluate(_dataSource, 100));

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT observed_close,observed_price,price_type FROM price_alert.triggers WHERE alert_id=$1", connection);
        command.Parameters.AddWithValue(alertId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(90m, reader.GetDecimal(0));
        Assert.Equal(110m, reader.GetDecimal(1));
        Assert.Equal("open", reader.GetString(2));
    }

    [Fact]
    public async Task Close_alert_records_the_published_close_price()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "close");
        await SeedBarAsync(new DateOnly(2026, 7, 16), 90m, 120m, 80m, 110m);

        Assert.Equal(1, await PriceAlertEngine.Evaluate(_dataSource, 100));

        Assert.Equal(110m, await ScalarAsync<decimal>($"SELECT observed_price FROM price_alert.triggers WHERE alert_id='{alertId}'"));
        Assert.Equal("close", await ScalarAsync<string>($"SELECT price_type FROM price_alert.triggers WHERE alert_id='{alertId}'"));
    }

    [Fact]
    public async Task Daily_high_and_low_never_trigger_an_alert()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "close");
        await SeedBarAsync(new DateOnly(2026, 7, 16), 90m, 150m, 50m, 90m);

        Assert.Equal(0, await PriceAlertEngine.Evaluate(_dataSource, 100));

        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}'"));
        Assert.Equal(new DateOnly(2026, 7, 16), await ScalarAsync<DateOnly>($"SELECT last_evaluated_date FROM price_alert.alerts WHERE id='{alertId}'"));
    }

    [Fact]
    public async Task Same_bar_is_skipped_and_a_newer_bar_is_evaluated()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "close");
        await SeedBarAsync(new DateOnly(2026, 7, 16), 90m, 95m, 85m, 90m);
        Assert.Equal(0, await PriceAlertEngine.Evaluate(_dataSource, 100));
        await ExecuteAsync($"UPDATE price_alert.alerts SET threshold=80 WHERE id='{alertId}'");

        Assert.Equal(0, await PriceAlertEngine.Evaluate(_dataSource, 100));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}'"));

        await SeedBarAsync(new DateOnly(2026, 7, 17), 95m, 100m, 90m, 95m);
        Assert.Equal(1, await PriceAlertEngine.Evaluate(_dataSource, 100));
        Assert.Equal(new DateOnly(2026, 7, 17), await ScalarAsync<DateOnly>($"SELECT trading_date FROM price_alert.triggers WHERE alert_id='{alertId}'"));
    }

    [Fact]
    public async Task Existing_same_day_trigger_prevents_a_duplicate()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "close");
        await SeedBarAsync(new DateOnly(2026, 7, 16), 110m, 120m, 100m, 110m);
        await ExecuteAsync($"INSERT INTO price_alert.triggers(id,alert_id,trading_date,observed_close,observed_price,price_type) VALUES ('{Guid.NewGuid()}','{alertId}','2026-07-16',110,110,'close')");

        Assert.Equal(0, await PriceAlertEngine.Evaluate(_dataSource, 100));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}' AND trading_date='2026-07-16'"));
        Assert.Equal(new DateOnly(2026, 7, 16), await ScalarAsync<DateOnly>($"SELECT last_evaluated_date FROM price_alert.alerts WHERE id='{alertId}'"));
    }

    [Fact]
    public async Task Unpublished_bar_is_not_evaluated()
    {
        await ResetAsync();
        var alertId = await SeedAlertAsync("above", 100m, "close");
        var runId = await ScalarAsync<Guid>("SELECT id FROM market.provider_runs LIMIT 1");
        await ExecuteAsync($"INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id,published_at) VALUES ('TEST','2026-07-16',150,160,140,150,1000,'test','{runId}',null)");

        Assert.Equal(0, await PriceAlertEngine.Evaluate(_dataSource, 100));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM price_alert.triggers WHERE alert_id='{alertId}'"));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM price_alert.alerts WHERE id='{alertId}' AND last_evaluated_date IS NOT NULL"));
    }

    private async Task ResetAsync()
    {
        await ExecuteAsync("TRUNCATE price_alert.triggers,price_alert.alerts,market.daily_bars,market.provider_runs,market.symbols CASCADE");
        await ExecuteAsync("INSERT INTO market.symbols(symbol,name,exchange,currency,timezone) VALUES ('TEST','Test','NYSE','USD','America/New_York')");
        var runId = Guid.NewGuid();
        await ExecuteAsync($"INSERT INTO market.provider_runs(id,provider,started_at,completed_at,status) VALUES ('{runId}','test',now(),now(),'succeeded')");
    }

    private async Task<Guid> SeedAlertAsync(string condition, decimal threshold, string evaluationPrice)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"INSERT INTO price_alert.alerts(id,user_id,symbol,condition_type,threshold,status,evaluation_price) VALUES ('{id}','{Guid.NewGuid()}','TEST','{condition}',{threshold},'active','{evaluationPrice}')");
        return id;
    }

    private async Task SeedBarAsync(DateOnly date, decimal open, decimal high, decimal low, decimal close)
    {
        var runId = await ScalarAsync<Guid>("SELECT id FROM market.provider_runs LIMIT 1");
        await ExecuteAsync($"INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id,published_at) VALUES ('TEST','{date:yyyy-MM-dd}',{open},{high},{low},{close},1000,'test','{runId}',now())");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (T)(await new NpgsqlCommand(sql, connection).ExecuteScalarAsync())!;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TradeDiary.slnx"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "platform/postgres/migrations");
    }
}
