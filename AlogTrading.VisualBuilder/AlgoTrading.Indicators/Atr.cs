using System;

namespace AlgoTrading.Indicators;

public static class Atr
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int period, Span<decimal?> output)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n < period + 1) return;

        decimal trSum = 0m;
        for (int i = 1; i <= period; i++)
            trSum += TrueRange(high[i], low[i], close[i - 1]);

        decimal atr = trSum / period;
        output[period] = atr;

        for (int i = period + 1; i < n; i++)
        {
            decimal tr = TrueRange(high[i], low[i], close[i - 1]);
            atr = (atr * (period - 1) + tr) / period;
            output[i] = atr;
        }
    }

    internal static decimal TrueRange(decimal h, decimal l, decimal prevC)
    {
        decimal a = h - l;
        decimal b = h - prevC;
        if (b < 0m) b = -b;
        decimal c = l - prevC;
        if (c < 0m) c = -c;
        decimal max = a;
        if (b > max) max = b;
        if (c > max) max = c;
        return max;
    }
}