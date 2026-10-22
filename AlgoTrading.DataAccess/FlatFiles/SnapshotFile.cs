using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.FlatFiles;

public class SnapshotFile
{
    public DateTime SnapshotDate { get; set; }
    public string AssetClass { get; set; } = "Equities"; 
    
    // The Core Instruments (Candles, Pivots, References)
    public Dictionary<long, Instruments> Instruments { get; set; } = new();

    // NEW: Key = "{InstrumentToken}_{Timeframe}_{IndicatorId}" (e.g., "256265_60_EMA_5")
    // Value = The calculated series array
    public Dictionary<string, List<decimal?>> IndicatorSeries { get; set; } = new();
}