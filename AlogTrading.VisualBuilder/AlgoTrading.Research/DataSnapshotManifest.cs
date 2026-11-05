using System;
using System.Collections.Generic;

namespace AlgoTrading.Research.Persistence;

public sealed class TimeframeSnapshotInfo
{
    public required string Timeframe { get; init; }
    public int BarCount { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public required string BarDataSha256 { get; init; }
}

public sealed class DataSnapshotManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string SnapshotId { get; init; }
    public required string OriginalStrategySha256 { get; init; }
    public uint InstrumentToken { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public Dictionary<string, TimeframeSnapshotInfo> Timeframes { get; init; } = new(StringComparer.Ordinal);
}