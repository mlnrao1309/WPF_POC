using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.Runtime;

public class RuntimeSnapshot
{
    public DateTime SnapshotDate { get; set; }
    
    // The Core domain objects (Candles, Pivots, References)
    public Dictionary<long, Instruments> Instruments { get; set; } = new();

    // The pre-calculated indicator series. 
    // Key: "{Token}_{Timeframe}_{IndicatorName}_{Period}" (e.g. "265_60_EMA_5")
    public Dictionary<string, List<decimal?>> IndicatorSeries { get; set; } = new();
}