namespace AlgoTrading.Models.MarketData.Dto;

/// <summary>
/// A pair of reference levels (Ref_Resistance / Ref_Support) from a single
/// trap-day pivot row, together with the date of the trap candle and the
/// timeframe it came from. Consumed by the strategy executor — not by the
/// screener.
/// </summary>
public sealed record RefLevels(
    long InstrumentToken,
    DateTime TrapDate,
    string TimeFrame,           // 'day' | 'week'
    decimal? RefResistance,
    decimal? RefSupport,

    // Optional: the trap-day OHLC, useful for stop placement.
    decimal? TrapOpen,
    decimal? TrapHigh,
    decimal? TrapLow,
    decimal? TrapClose,

    // Optional: standard levels from that same row, in case the strategy
    // wants context (e.g. "trap was at R1, target is R2").
    decimal? PP,
    decimal? R1, decimal? R2, decimal? R3,
    decimal? S1, decimal? S2, decimal? S3);