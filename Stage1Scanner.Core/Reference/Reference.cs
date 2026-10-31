namespace Stage1Scanner.Core.Market;

/// <summary>
/// A stateful snapshot captured the moment a trigger expression first
/// evaluated True on a closed bar. Unlike an Anchor (which is tied to a
/// CriticalLevel break), a Reference can be produced by any rule expression.
/// The captured bar's OHLC and close time are exposed as operands so
/// subsequent rules can reason about "the bar where X happened".
/// </summary>
public sealed record Reference(
    string Id,
    Timeframe Timeframe,
    DateTimeOffset BarOpenTime,
    DateTimeOffset BarCloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? CapturedValue, // <-- NEW
    DateTimeOffset CapturedAtEvaluationTime);