public sealed class PriceAlertEngineTests
{
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
