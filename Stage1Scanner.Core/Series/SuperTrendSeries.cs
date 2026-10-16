namespace Stage1Scanner.Core.Series;

public sealed class SuperTrendSeries
{
    public required NumericSeries Value { get; init; }
    public required SuperTrendDirection?[] Direction { get; init; }

    /// <summary>
    /// True on the single bar where Direction flipped from Bearish to Bullish.
    /// SCN-IND-002: state can stay Bullish for many bars while TurnedBullish
    /// is only true on the transition bar.
    /// </summary>
    public required bool[] TurnedBullish { get; init; }

    /// <summary>
    /// True on the single bar where Direction flipped from Bullish to Bearish.
    /// </summary>
    public required bool[] TurnedBearish { get; init; }
}
