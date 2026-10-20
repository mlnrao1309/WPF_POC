using AlgoTrading.Models;

namespace AlgoTrading.Aggregation.Tests;

internal static class TestDataFactory
{
    public const int IstOffsetMinutes = 330;

    /// <summary>
    /// Build a full NSE session starting at 09:15 IST for the given day,
    /// with the given base timeframe (minutes). Bars are placed so the
    /// last bar starts strictly before 15:30 IST.
    /// </summary>
    public static TestBarSeries BuildSession(DateOnly day, int baseMinutes)
    {
        int totalMinutes = 375;
        int barCount = totalMinutes / baseMinutes;

        var istStart = day.ToDateTime(new TimeOnly(9, 15, 0));
        var utcStart = istStart.AddMinutes(-IstOffsetMinutes);

        var dt = new DateTime[barCount];
        var op = new decimal[barCount];
        var hi = new decimal[barCount];
        var lo = new decimal[barCount];
        var cl = new decimal[barCount];
        var vo = new decimal[barCount];
        var oi = new decimal[barCount];

        for (int i = 0; i < barCount; i++)
        {
            dt[i] = utcStart.AddMinutes(i * baseMinutes);
            decimal b = 100m + i;
            op[i] = b;
            hi[i] = b + 1m;
            lo[i] = b - 1m;
            cl[i] = b + 0.5m;
            vo[i] = 1000m + i;
            oi[i] = 0m;
        }

        var tf = new Timeframe(TimeframeUnit.m, baseMinutes);
        return new TestBarSeries(2885u, "TEST", tf, op, hi, lo, cl, vo, oi, dt);
    }

    /// <summary>
    /// Build a daily bar series from a list of IST calendar dates. Each
    /// bar's timestamp is 09:15 IST on that date (converted to UTC).
    /// </summary>
    public static TestBarSeries BuildDaily(params DateOnly[] istDates)
    {
        int n = istDates.Length;
        var dt = new DateTime[n];
        var op = new decimal[n];
        var hi = new decimal[n];
        var lo = new decimal[n];
        var cl = new decimal[n];
        var vo = new decimal[n];
        var oi = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            var ist = istDates[i].ToDateTime(new TimeOnly(9, 15, 0));
            dt[i] = ist.AddMinutes(-IstOffsetMinutes);
            decimal b = 100m + i;
            op[i] = b;
            hi[i] = b + 1m;
            lo[i] = b - 1m;
            cl[i] = b + 0.5m;
            vo[i] = 1000m + i;
            oi[i] = 0m;
        }

        return new TestBarSeries(2885u, "TEST", new Timeframe(TimeframeUnit.D, 1),
            op, hi, lo, cl, vo, oi, dt);
    }
}
