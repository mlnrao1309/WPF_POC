using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Supertrend
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int period, decimal multiplier,
        Span<decimal?> line, Span<decimal?> direction)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (line.Length != n || direction.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        line.Clear();
        direction.Clear();
        if (n < period + 1) return;

        var atrArr = Scratch.RentNullable(n);
        try
        {
            var atr = atrArr.AsSpan(0, n);
            Atr.Calculate(high, low, close, period, atr);

            int first = period;
            decimal prevClose = close[first - 1];
            decimal mid = (high[first] + low[first]) / 2m;
            decimal finalUp = mid + multiplier * atr[first]!.Value;
            decimal finalDn = mid - multiplier * atr[first]!.Value;
            int dir = close[first] >= mid ? 1 : -1;

            line[first] = dir == 1 ? finalDn : finalUp;
            direction[first] = dir;

            for (int i = first + 1; i < n; i++)
            {
                mid = (high[i] + low[i]) / 2m;
                decimal up = mid + multiplier * atr[i]!.Value;
                decimal dn = mid - multiplier * atr[i]!.Value;

                decimal prevFinalUp = finalUp;
                decimal prevFinalDn = finalDn;

                finalUp = (up < prevFinalUp || prevClose > prevFinalUp) ? up : prevFinalUp;
                finalDn = (dn > prevFinalDn || prevClose < prevFinalDn) ? dn : prevFinalDn;

                if (dir == 1 && close[i] < finalDn) dir = -1;
                else if (dir == -1 && close[i] > finalUp) dir = 1;

                line[i] = dir == 1 ? finalDn : finalUp;
                direction[i] = dir;

                prevClose = close[i];
            }
        }
        finally { Scratch.Return(atrArr); }
    }
}