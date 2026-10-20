using System;

namespace AlgoTrading.Indicators;

public static class PivotPointsClassic
{
    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        ReadOnlySpan<DateTime> dateTime,
        Span<decimal?> pp, Span<decimal?> r1, Span<decimal?> r2, Span<decimal?> r3,
        Span<decimal?> s1, Span<decimal?> s2, Span<decimal?> s3)
    {
        int n = close.Length;
        if (high.Length != n || low.Length != n || dateTime.Length != n)
            throw new ArgumentException("Input spans must be equal length.");
        if (pp.Length != n || r1.Length != n || r2.Length != n || r3.Length != n
            || s1.Length != n || s2.Length != n || s3.Length != n)
            throw new ArgumentException("Output spans must equal source length.");

        pp.Clear(); r1.Clear(); r2.Clear(); r3.Clear();
        s1.Clear(); s2.Clear(); s3.Clear();
        if (n < 2) return;

        int curDay = int.MinValue;
        decimal curH = 0m, curL = 0m, curC = 0m;
        decimal prevH = 0m, prevL = 0m, prevC = 0m;
        bool havePrev = false;

        for (int i = 0; i < n; i++)
        {
            int key = Vwap.TradingDayKey(dateTime[i]);
            if (key != curDay)
            {
                if (curDay != int.MinValue)
                {
                    prevH = curH;
                    prevL = curL;
                    prevC = curC;
                    havePrev = true;
                }
                curDay = key;
                curH = high[i];
                curL = low[i];
                curC = close[i];
            }
            else
            {
                if (high[i] > curH) curH = high[i];
                if (low[i] < curL) curL = low[i];
                curC = close[i];
            }

            if (!havePrev) continue;

            decimal p = (prevH + prevL + prevC) / 3m;
            pp[i] = p;
            r1[i] = 2m * p - prevL;
            s1[i] = 2m * p - prevH;
            r2[i] = p + (prevH - prevL);
            s2[i] = p - (prevH - prevL);
            r3[i] = prevH + 2m * (p - prevL);
            s3[i] = prevL - 2m * (prevH - p);
        }
    }
}