namespace AlgoTrading.Backtest;

public static class Dec
{
    // decimal has no NaN. Use MinValue as the sentinel. Never reaches arithmetic
    // unguarded â€” every operation checks Dec.IsNaN first.
    public const decimal NaN = decimal.MinValue;
    public static bool IsNaN(decimal v) => v == NaN;
}