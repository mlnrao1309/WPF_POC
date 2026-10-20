using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class KeltnerChannel
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int period, int atrPeriod, decimal multiplier,
        Span<decimal?> upper, Span<decimal?> middle, Span<decimal?> lower)
    {
        if (period < 1 || atrPeriod < 1)
            throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (upper.Length != n || middle.Length != n || lower.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        upper.Clear();
        middle.Clear();
        lower.Clear();
        if (n == 0) return;

        var emaArr = Scratch.RentNullable(n);
        var atrArr = Scratch.RentNullable(n);
        try
        {
            var ema = emaArr.AsSpan(0, n);
            var atr = atrArr.AsSpan(0, n);
            Ema.Calculate(close, period, ema);
            Atr.Calculate(high, low, close, atrPeriod, atr);

            for (int i = 0; i < n; i++)
            {
                if (!ema[i].HasValue || !atr[i].HasValue) continue;
                decimal m = ema[i]!.Value;
                decimal band = multiplier * atr[i]!.Value;
                middle[i] = m;
                upper[i] = m + band;
                lower[i] = m - band;
            }
        }
        finally
        {
            Scratch.Return(emaArr);
            Scratch.Return(atrArr);
        }
    }
}