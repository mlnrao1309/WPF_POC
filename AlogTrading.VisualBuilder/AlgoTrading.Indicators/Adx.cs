using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Adx
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        int period,
        Span<decimal?> adx, Span<decimal?> plusDi, Span<decimal?> minusDi)
    {
        if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
        int n = close.Length;
        if (high.Length != n || low.Length != n)
            throw new ArgumentException("OHLC spans must be equal length.");
        if (adx.Length != n || plusDi.Length != n || minusDi.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        adx.Clear();
        plusDi.Clear();
        minusDi.Clear();
        if (n < 2 * period + 1) return;

        var trArr = Scratch.RentDecimal(n);
        var pDmArr = Scratch.RentDecimal(n);
        var mDmArr = Scratch.RentDecimal(n);
        var dxArr = Scratch.RentDecimal(n);
        try
        {
            var tr = trArr.AsSpan(0, n);
            var pDm = pDmArr.AsSpan(0, n);
            var mDm = mDmArr.AsSpan(0, n);
            var dx = dxArr.AsSpan(0, n);

            for (int i = 1; i < n; i++)
            {
                tr[i] = Atr.TrueRange(high[i], low[i], close[i - 1]);

                decimal up = high[i] - high[i - 1];
                decimal down = low[i - 1] - low[i];
                pDm[i] = (up > down && up > 0m) ? up : 0m;
                mDm[i] = (down > up && down > 0m) ? down : 0m;
            }

            decimal trS = 0m, pS = 0m, mS = 0m;
            for (int i = 1; i <= period; i++)
            {
                trS += tr[i];
                pS += pDm[i];
                mS += mDm[i];
            }

            int warmup = period;
            int adxWarmup = 2 * period;

            EmitDi(warmup, trS, pS, mS, plusDi, minusDi, dx);

            for (int i = period + 1; i < n; i++)
            {
                trS = trS - trS / period + tr[i];
                pS = pS - pS / period + pDm[i];
                mS = mS - mS / period + mDm[i];
                EmitDi(i, trS, pS, mS, plusDi, minusDi, dx);
            }

            decimal dxSum = 0m;
            for (int i = warmup + 1; i <= adxWarmup; i++) dxSum += dx[i];
            decimal adxVal = dxSum / period;
            adx[adxWarmup] = adxVal;

            for (int i = adxWarmup + 1; i < n; i++)
            {
                adxVal = (adxVal * (period - 1) + dx[i]) / period;
                adx[i] = adxVal;
            }
        }
        finally
        {
            Scratch.Return(trArr);
            Scratch.Return(pDmArr);
            Scratch.Return(mDmArr);
            Scratch.Return(dxArr);
        }
    }

    private static void EmitDi(
        int i, decimal trS, decimal pS, decimal mS,
        Span<decimal?> plusDi, Span<decimal?> minusDi, Span<decimal> dx)
    {
        if (trS == 0m)
        {
            plusDi[i] = 0m;
            minusDi[i] = 0m;
            dx[i] = 0m;
            return;
        }

        decimal p = 100m * pS / trS;
        decimal m = 100m * mS / trS;
        plusDi[i] = p;
        minusDi[i] = m;

        decimal sum = p + m;
        dx[i] = sum == 0m ? 0m : 100m * Math.Abs(p - m) / sum;
    }
}