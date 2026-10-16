namespace Stage1Scanner.Core.Market;

public enum TimeframeUnit
{
    Minute,
    Hour,
    Day,
    Week,
    Month
}

public readonly record struct Timeframe(TimeframeUnit Unit, int Count = 1)
{
    public static Timeframe Minutes(int count) => new(TimeframeUnit.Minute, count);
    public static Timeframe Hours(int count) => new(TimeframeUnit.Hour, count);
    public static Timeframe Day => new(TimeframeUnit.Day, 1);
    public static Timeframe Week => new(TimeframeUnit.Week, 1);
    public static Timeframe Month => new(TimeframeUnit.Month, 1);

    public override string ToString() => Unit switch
    {
        TimeframeUnit.Minute => $"{Count}m",
        TimeframeUnit.Hour => $"{Count}H",
        TimeframeUnit.Day => Count == 1 ? "D" : $"{Count}D",
        TimeframeUnit.Week => Count == 1 ? "W" : $"{Count}W",
        TimeframeUnit.Month => Count == 1 ? "M" : $"{Count}M",
        _ => throw new ArgumentOutOfRangeException()
    };
}
