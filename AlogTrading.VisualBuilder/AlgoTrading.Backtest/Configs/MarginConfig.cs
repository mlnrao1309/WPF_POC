using System.Text.Json;

namespace AlgoTrading.Backtest.Configs;

public sealed class MarginConfig
{
    public int SchemaVersion { get; set; } = 1;
    public MarginEquityBlock Equity { get; set; } = new();

    public static MarginConfig Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults();
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MarginConfig>(json, opts) ?? Defaults();
        }
        catch { return Defaults(); }
    }

    public static MarginConfig Defaults() => new()
    {
        Equity = new MarginEquityBlock
        {
            Delivery = new MarginModeBlock { MarginPercent = 100m },
            Intraday = new MarginModeBlock { MarginPercent = 20m }
        }
    };
}

public sealed class MarginEquityBlock
{
    public MarginModeBlock Delivery { get; set; } = new() { MarginPercent = 100m };
    public MarginModeBlock Intraday { get; set; } = new() { MarginPercent = 20m };
}

public sealed class MarginModeBlock
{
    public decimal MarginPercent { get; set; } = 100m;
}