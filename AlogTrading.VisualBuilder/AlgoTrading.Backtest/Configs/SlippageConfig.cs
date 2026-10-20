using System.Text.Json;

namespace AlgoTrading.Backtest.Configs;

public sealed class SlippageConfig
{
    public int SchemaVersion { get; set; } = 1;
    public SlippageEquityBlock Equity { get; set; } = new();

    public static SlippageConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults();
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<SlippageConfig>(json, opts) ?? Defaults();
        }
        catch { return Defaults(); }
    }

    public static SlippageConfig Defaults() => new()
    {
        Equity = new SlippageEquityBlock
        {
            Model = "Spread",
            SpreadPercent = 0.05m,
            FixedAmount = 0m
        }
    };
}

public sealed class SlippageEquityBlock
{
    public string Model { get; set; } = "Spread";
    public decimal SpreadPercent { get; set; } = 0.05m;
    public decimal FixedAmount { get; set; }
}