using System.Text.Json;

record PositionSizing(decimal AccountValue,decimal RiskPercent,decimal EntryPrice,decimal StopPrice);
record RiskReward(decimal EntryPrice,decimal StopPrice,decimal TargetPrice)
{
    public static bool IsValid(RiskReward x) => x.StopPrice < x.EntryPrice && x.TargetPrice > x.EntryPrice || x.StopPrice > x.EntryPrice && x.TargetPrice < x.EntryPrice;
}
record AverageCost(decimal CurrentQuantity,decimal CurrentAverageCost,decimal AddedQuantity,decimal AddedPrice);
record ProfitLoss(string Side,decimal EntryPrice,decimal ExitPrice,decimal Quantity,decimal EntryFee,decimal ExitFee);
record PositionSizingResponse(decimal Quantity,decimal PlannedLoss,decimal RiskBudget,decimal PositionValue,decimal PerUnitRisk)
{
    public static PositionSizingResponse Calculate(PositionSizing x) { var budget=x.AccountValue*x.RiskPercent/100m;var risk=Math.Abs(x.EntryPrice-x.StopPrice);var qty=decimal.Floor(budget/risk);return new(qty,decimal.Round(qty*risk,2),decimal.Round(budget,2),decimal.Round(qty*x.EntryPrice,2),decimal.Round(risk,6)); }
}
record RiskRewardResponse(decimal Ratio,decimal RiskPerUnit,decimal RewardPerUnit,decimal BreakevenWinRate)
{
    public static RiskRewardResponse Calculate(RiskReward x) { var risk=Math.Abs(x.EntryPrice-x.StopPrice);var reward=Math.Abs(x.TargetPrice-x.EntryPrice);var ratio=reward/risk;return new(decimal.Round(ratio,4),decimal.Round(risk,6),decimal.Round(reward,6),decimal.Round(100m/(1m+ratio),2)); }
}
record AverageCostResponse(decimal AverageCost,decimal TotalQuantity,decimal TotalCost,decimal AverageCostChange)
{
    public static AverageCostResponse Calculate(AverageCost x) { var qty=x.CurrentQuantity+x.AddedQuantity;var cost=x.CurrentQuantity*x.CurrentAverageCost+x.AddedQuantity*x.AddedPrice;var avg=cost/qty;return new(decimal.Round(avg,6),decimal.Round(qty,6),decimal.Round(cost,2),decimal.Round((avg/x.CurrentAverageCost-1m)*100m,2)); }
}
record ProfitLossResponse(decimal NetPnl,decimal ReturnPercent,decimal GrossPnl,decimal TotalFees,decimal ExitValue)
{
    public static ProfitLossResponse Calculate(ProfitLoss x) { var direction=x.Side=="short"?-1m:1m;var gross=(x.ExitPrice-x.EntryPrice)*x.Quantity*direction;var fees=x.EntryFee+x.ExitFee;var net=gross-fees;return new(decimal.Round(net,2),decimal.Round(net/(x.EntryPrice*x.Quantity+x.EntryFee)*100m,2),decimal.Round(gross,2),decimal.Round(fees,2),decimal.Round(x.ExitPrice*x.Quantity,2)); }
}

record PresetWrite(string Name,string ToolType,JsonElement Inputs,string? Currency);
record PresetResponse(Guid Id,string Name,string ToolType,JsonElement Inputs,string? Currency,DateTime? LastUsedAt,DateTime CreatedAt,DateTime UpdatedAt);
record SavedCalculationWrite(string ToolType,JsonElement Inputs,string Currency,string? Symbol,Guid? SourceDiaryId,Guid? SourceTransactionId,string? Note);
record SavedCalculationResponse(Guid Id,string ToolType,int SchemaVersion,JsonElement Inputs,JsonElement Output,string Currency,string? Symbol,Guid? SourceDiaryId,Guid? SourceTransactionId,string? Note,DateTime CreatedAt);
record ToolCollection<T>(IReadOnlyList<T> Items);
