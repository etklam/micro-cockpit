public sealed class PriceAlertEngineTests
{
    [Fact]
    public void Above_and_below_use_the_selected_published_daily_bar_price()
    {
        var bar = new PublishedPriceBar(new DateOnly(2026, 7, 16), 90m, 110m);

        Assert.False(PriceAlertEngine.ReachedThreshold(bar, "open", "above", 100m));
        Assert.True(PriceAlertEngine.ReachedThreshold(bar, "close", "above", 100m));
        Assert.True(PriceAlertEngine.ReachedThreshold(bar, "open", "below", 100m));
        Assert.False(PriceAlertEngine.ReachedThreshold(bar, "close", "below", 100m));
    }

    [Fact]
    public void Only_a_newer_published_bar_is_evaluated()
    {
        var latest = new DateOnly(2026, 7, 16);

        Assert.True(PriceAlertEngine.ShouldEvaluate(latest, null));
        Assert.True(PriceAlertEngine.ShouldEvaluate(latest, latest.AddDays(-1)));
        Assert.False(PriceAlertEngine.ShouldEvaluate(latest, latest));
        Assert.False(PriceAlertEngine.ShouldEvaluate(latest, latest.AddDays(1)));
    }

    [Fact]
    public void Omitted_evaluation_price_defaults_to_close()
    {
        Assert.Equal("close", PriceAlertEngine.EvaluationPriceOrDefault(null));
        Assert.Equal("close", PriceAlertEngine.EvaluationPriceOrDefault(""));
        Assert.Equal("open", PriceAlertEngine.EvaluationPriceOrDefault("open"));
    }

    [Fact]
    public void Worker_interval_never_drops_below_one_hour()
    {
        Assert.Equal(3600, PriceAlertEngine.WorkerIntervalSeconds(null));
        Assert.Equal(3600, PriceAlertEngine.WorkerIntervalSeconds(60));
        Assert.Equal(7200, PriceAlertEngine.WorkerIntervalSeconds(7200));
    }

    [Fact]
    public void Detects_an_upward_moving_average_cross()
    {
        var bars = new[] { new Bar(new DateOnly(2026, 7, 3), 120m), new Bar(new DateOnly(2026, 7, 2), 80m), new Bar(new DateOnly(2026, 7, 1), 100m) };
        Assert.True(PriceAlertEngine.Crossed(bars, 2, "above"));
    }

    [Fact]
    public void Does_not_trigger_with_insufficient_history()
    {
        var bars = new[] { new Bar(new DateOnly(2026, 7, 3), 120m), new Bar(new DateOnly(2026, 7, 2), 90m) };
        Assert.False(PriceAlertEngine.Crossed(bars, 2, "above"));
    }
}
