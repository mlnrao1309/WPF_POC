using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.FlatFiles;

public class SnapshotFile
{
    public int FormatVersion { get; set; }
    public List<string> RequiredTimeframes { get; set; } = new();
    public Guid Generation { get; set; }
    public string DependencyFingerprint { get; set; } = string.Empty;
    public List<AlgoTrading.DataAccess.Etl.RequiredIndicator> IndicatorRequirements { get; set; } = new();
    public DateTime SnapshotDate { get; set; }
    public string AssetClass { get; set; } = "Equities"; 
    
    // The Core Instruments (Candles, Pivots, References)
    public Dictionary<long, Instruments> Instruments { get; set; } = new();

    // Key = token_timeframe_indicator_period_input_factor_output (format version 2).
    // Value = The calculated series array
    public Dictionary<string, List<decimal?>> IndicatorSeries { get; set; } = new();
}