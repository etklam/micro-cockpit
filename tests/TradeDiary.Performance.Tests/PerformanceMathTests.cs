public sealed class PerformanceMathTests
{
    [Fact]
    public void Calculates_percentage_from_the_production_formula()
    {
        Assert.Equal(12.3457m, PerformanceMath.PnlPercentage(123.4567m, 1000m));
    }

    [Fact]
    public void Missing_or_non_positive_capital_has_no_percentage()
    {
        Assert.Null(PerformanceMath.PnlPercentage(10m, null));
        Assert.Null(PerformanceMath.PnlPercentage(10m, 0m));
    }
}
