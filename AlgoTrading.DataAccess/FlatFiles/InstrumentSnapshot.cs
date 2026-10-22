namespace AlgoTrading.DataAccess.FlatFiles;

public class InstrumentSnapshot
{
    public long InstrumentToken { get; set; }
    public string Symbol { get; set; } = string.Empty;
    
    // Null for Equities. Populated for F&O.
    public DateTime? ExpiryDate { get; set; }

    // Key: Timeframe string (e.g., "60", "15", "D")
    public Dictionary<string, TimeframeSnapshot> Timeframes { get; set; } = new();
}