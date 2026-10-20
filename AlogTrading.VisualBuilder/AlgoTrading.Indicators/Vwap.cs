using System;

namespace AlgoTrading.Indicators;

public static class Vwap
{
    /// <summary>NSE session start 09:15 IST = 03:45 UTC.</summary>
    private static readonly TimeSpan SessionStartUtc = new TimeSpan(3, 45, 0);

    public static void Calculate(
        ReadOnlySpan<decimal> high, ReadOnlySpan<decimal> low, ReadOnlySpan<decimal> close,
        ReadOnlySpan<decimal> volume, ReadOnlySpan<DateTime> dateTime,
        Span<decimal?> output)
    {
        int n = close.Length;
        if (high.Length != n || low.Length != n || volume.Length != n || dateTime.Length != n)
            throw new ArgumentException("All input spans must be equal length.");
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        if (n == 0) return;

        decimal cumPv = 0m, cumV = 0m;
        int currentDay = int.MinValue;

        for (int i = 0; i < n; i++)
        {
            int dayKey = TradingDayKey(dateTime[i]);
            if (dayKey != currentDay)
            {
                currentDay = dayKey;
                cumPv = 0m;
                cumV = 0m;
            }

            decimal tp = (high[i] + low[i] + close[i]) / 3m;
            decimal v = volume[i];
            cumPv += tp * v;
            cumV += v;

            output[i] = cumV == 0m ? null : cumPv / cumV;
        }
    }

    /// <summary>
    /// Groups bars by IST trading day. Bars whose UTC time-of-day is before
    /// 03:45 UTC are shifted to the previous calendar day so that any bar
    /// timestamped at the UTC-midnight boundary of a daily series still
    /// resolves to the correct IST session.
    /// </summary>
    public static int TradingDayKey(DateTime utc)
    {
        if (utc.TimeOfDay < SessionStartUtc)
            utc = utc.AddDays(-1);
        return utc.Year * 10000 + utc.Month * 100 + utc.Day;
    }
}
