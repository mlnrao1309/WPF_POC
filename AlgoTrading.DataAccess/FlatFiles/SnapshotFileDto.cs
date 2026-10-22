namespace AlgoTrading.DataAccess.FlatFiles;

public enum AssetClass
{
    Equities,
    FnO
}

public class SnapshotFileDto
{
    public DateTime SnapshotDate { get; set; }
    public AssetClass AssetClass { get; set; }
    
    // Key: InstrumentToken
    public Dictionary<long, InstrumentSnapshot> Instruments { get; set; } = new();
}