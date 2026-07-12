public static class PerformanceMath
{
    public static decimal? PnlPercentage(decimal pnlAmount, decimal? capitalBase) =>
        capitalBase is > 0 ? decimal.Round(pnlAmount / capitalBase.Value * 100, 4) : null;
}
