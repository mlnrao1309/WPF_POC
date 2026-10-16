namespace Stage1Scanner.Core.Market;

public readonly record struct Candle(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? OpenInterest = null)
{
    /// <summary>
    /// Constructs a Candle whose CloseTime is derived from
    /// OpenTime + <paramref name="timeframe"/>. Use this from loaders;
    /// the positional constructor remains for tests that need explicit
    /// CloseTime values.
    /// </summary>
    public static Candle Create(
        DateTimeOffset openTime,
        Timeframe timeframe,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal? openInterest = null)
        => new(
            openTime,
            CandleInterval.CloseTime(openTime, timeframe),
            open,
            high,
            low,
            close,
            volume,
            openInterest);
}