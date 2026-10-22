namespace AlgoTrading.DataAccess.FlatFiles;

public class TimeframeSnapshot
{
    public List<CandleDto> Candles { get; set; } = new();

    // Key: Indicator ID (e.g., "EMA_5", "RSI_14")
    // Value: The calculated series array (aligned by index with the Candles list)
    public Dictionary<string, List<decimal?>> Indicators { get; set; } = new();
}