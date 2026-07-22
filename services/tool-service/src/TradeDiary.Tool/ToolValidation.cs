using System.Text.Json;

static class ToolValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly string[] Types = ["position-sizing","risk-reward","average-cost","profit-loss"];
    internal static bool ValidCurrency(string? value) => value is not null && value.Length == 3 && value.All(char.IsAsciiLetter);
    internal static bool ValidSymbol(string? value) => value is null || value.Length is >= 1 and <= 20 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    /// <summary>
    /// Deserializes inputs through the declared tool schema and recalculates the authoritative
    /// output. Saved-calculation requests never provide a trusted frontend output value.
    /// </summary>
    internal static bool TryCalculate(string tool, JsonElement inputs, out object? output)
    {
        output = null;
        try
        {
            output = tool switch
            {
                "position-sizing" => Calculate(inputs.Deserialize<PositionSizing>(JsonOptions)!),
                "risk-reward" => Calculate(inputs.Deserialize<RiskReward>(JsonOptions)!),
                "average-cost" => Calculate(inputs.Deserialize<AverageCost>(JsonOptions)!),
                "profit-loss" => Calculate(inputs.Deserialize<ProfitLoss>(JsonOptions)!),
                _ => null
            };
            return output is not null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }

    /// <summary>
    /// Validates a reusable, possibly partial input set. Unknown keys are rejected so JSON
    /// storage remains a versioned tool contract rather than an arbitrary settings document.
    /// </summary>
    internal static bool ValidPreset(PresetWrite input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 80 || !Types.Contains(input.ToolType) || input.Inputs.ValueKind != JsonValueKind.Object) return false;
        if (input.Currency is not null && !ValidCurrency(input.Currency)) return false;
        var allowed = input.ToolType switch
        {
            "position-sizing" => new HashSet<string>(["accountValue","riskPercent","entryPrice","stopPrice"]),
            "risk-reward" => new HashSet<string>(["entryPrice","stopPrice","targetPrice"]),
            "average-cost" => new HashSet<string>(["currentQuantity","currentAverageCost","addedQuantity","addedPrice"]),
            _ => new HashSet<string>(["side","entryPrice","exitPrice","quantity","entryFee","exitFee"]),
        };
        foreach (var property in input.Inputs.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) return false;
            if (property.Name == "side") { if (property.Value.GetString() is not ("long" or "short")) return false; continue; }
            if (!property.Value.TryGetDecimal(out var number) || (property.Name is "entryFee" or "exitFee" ? number < 0 : number <= 0)) return false;
            if (property.Name == "riskPercent" && number > 100) return false;
        }
        return input.Inputs.EnumerateObject().Any();
    }

    private static object? Calculate(PositionSizing x) => x.AccountValue<=0||x.RiskPercent<=0||x.RiskPercent>100||x.EntryPrice<=0||x.StopPrice<=0||x.EntryPrice==x.StopPrice ? null : PositionSizingResponse.Calculate(x);
    private static object? Calculate(RiskReward x) => x.EntryPrice<=0||x.StopPrice<=0||x.TargetPrice<=0||!RiskReward.IsValid(x) ? null : RiskRewardResponse.Calculate(x);
    private static object? Calculate(AverageCost x) => x.CurrentQuantity<=0||x.CurrentAverageCost<=0||x.AddedQuantity<=0||x.AddedPrice<=0 ? null : AverageCostResponse.Calculate(x);
    private static object? Calculate(ProfitLoss x) => x.Side is not ("long" or "short")||x.EntryPrice<=0||x.ExitPrice<=0||x.Quantity<=0||x.EntryFee<0||x.ExitFee<0 ? null : ProfitLossResponse.Calculate(x);
}
