namespace AlgoTrading.Screener.Storage;

/// <summary>
/// Persisted state of the screener window between sessions. Every field is
/// nullable so a partial file (e.g. written by an older app version) can be
/// read without exceptions.
/// </summary>
public sealed record WindowSettings(
    string?   LastQuery = null,
    string?   Timeframe = null,
    DateTime? FromDate  = null,
    DateTime? ToDate    = null,
    string?   Segment   = null,
    string?   Exchange  = null)
{
    public static readonly WindowSettings Empty = new();
}