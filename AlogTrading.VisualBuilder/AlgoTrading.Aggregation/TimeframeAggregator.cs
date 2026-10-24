using System.Runtime.CompilerServices;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Aggregation;

/// <summary>
/// Aggregates base-timeframe bars to higher timeframes per Section 3.8.
/// Intraday targets require integer-multiple bases; D/W/M use daily base
/// directly. Intraday windows start at the first supplied candle of each IST date.
/// Every supplied candle participates, including shortened and special sessions.
/// </summary>
public static class TimeframeAggregator
{
    private static readonly Timeframe DailyTimeframe = new Timeframe(TimeframeUnit.D, 1);

    public static IBarSeries Aggregate(IBarSeries baseSeries, Timeframe target)
    {
        ArgumentNullException.ThrowIfNull(baseSeries);

        var baseTf = baseSeries.Timeframe;

        if (baseTf.Unit == target.Unit && baseTf.Count == target.Count)
            return baseSeries;

        if (baseSeries.Count == 0)
            return new ColumnarBarSeries(
                baseSeries.InstrumentToken, baseSeries.Symbol, target,
                Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
                Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
                Array.Empty<DateTime>());

        return target.Unit switch
        {
            TimeframeUnit.m or TimeframeUnit.H => AggregateIntraday(baseSeries, target),
            TimeframeUnit.D                     => AggregateDaily(baseSeries, target),
            TimeframeUnit.W or TimeframeUnit.M  => AggregatePositional(baseSeries, target),
            _ => throw new InvalidOperationException(
                $"Unsupported target timeframe '{target}'")
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ToMinutes(Timeframe tf) => tf.Unit switch
    {
        TimeframeUnit.m => tf.Count,
        TimeframeUnit.H => tf.Count * 60,
        _ => throw new InvalidOperationException($"Not an intraday timeframe: {tf}")
    };

    // ----------------------------------------------------------------
    // Intraday aggregation (m/H -> m/H)
    // ----------------------------------------------------------------
    private static IBarSeries AggregateIntraday(IBarSeries b, Timeframe target)
    {
        var baseTf = b.Timeframe;
        if (baseTf.Unit != TimeframeUnit.m && baseTf.Unit != TimeframeUnit.H)
            throw new InvalidOperationException(
                $"Intraday aggregation requires an intraday base; got {baseTf}");

        int baseMinutes   = ToMinutes(baseTf);
        int targetMinutes = ToMinutes(target);

        if (targetMinutes < baseMinutes || targetMinutes % baseMinutes != 0)
            throw new InvalidOperationException(
                $"Base {baseTf} cannot aggregate to {target}: not an integer multiple");

        int barsPerBucket = targetMinutes / baseMinutes;
        int count         = b.Count;

        ReadOnlySpan<DateTime> dt = b.DateTime.Span;
        ReadOnlySpan<decimal>  op = b.Open.Span;
        ReadOnlySpan<decimal>  hi = b.High.Span;
        ReadOnlySpan<decimal>  lo = b.Low.Span;
        ReadOnlySpan<decimal>  cl = b.Close.Span;
        ReadOnlySpan<decimal>  vo = b.Volume.Span;
        ReadOnlySpan<decimal>  oi = b.OI.Span;

        int cap = count / barsPerBucket + 1;
        var outDt = new List<DateTime>(cap);
        var outOp = new List<decimal>(cap);
        var outHi = new List<decimal>(cap);
        var outLo = new List<decimal>(cap);
        var outCl = new List<decimal>(cap);
        var outVo = new List<decimal>(cap);
        var outOi = new List<decimal>(cap);

        int i = 0;
        DateTime dayKey = default;
        DateTime dayStart = default;
        while (i < count)
        {
            DateTime ist = ToIst(dt[i]);
            if (ist.Date != dayKey)
            {
                dayKey = ist.Date;
                dayStart = ist;
            }
            long bucket = (ist.Ticks - dayStart.Ticks) / TimeSpan.TicksPerMinute / targetMinutes;

            int j = i;
            while (j < count)
            {
                DateTime cur = ToIst(dt[j]);
                if (cur.Date != dayKey) break;
                if ((cur.Ticks - dayStart.Ticks) / TimeSpan.TicksPerMinute / targetMinutes != bucket) break;
                j++;
            }

            decimal fOp = op[i];
            decimal fHi = hi[i];
            decimal fLo = lo[i];
            decimal fVo = vo[i];
            for (int k = i + 1; k < j; k++)
            {
                decimal hk = hi[k];
                if (hk > fHi) fHi = hk;
                decimal lk = lo[k];
                if (lk < fLo) fLo = lk;
                fVo += vo[k];
            }
            decimal fCl = cl[j - 1];
            decimal fOi = oi[j - 1];

            outDt.Add(dt[i]);
            outOp.Add(fOp);
            outHi.Add(fHi);
            outLo.Add(fLo);
            outCl.Add(fCl);
            outVo.Add(fVo);
            outOi.Add(fOi);

            i = j;
        }

        return new ColumnarBarSeries(
            b.InstrumentToken, b.Symbol, target,
            outOp.ToArray(), outHi.ToArray(), outLo.ToArray(),
            outCl.ToArray(), outVo.ToArray(), outOi.ToArray(),
            outDt.ToArray());
    }

    // ----------------------------------------------------------------
    // Daily aggregation (intraday -> D, or D -> D passthrough)
    // ----------------------------------------------------------------
    private static IBarSeries AggregateDaily(IBarSeries b, Timeframe target)
    {
        var baseTf = b.Timeframe;
        if (baseTf.Unit == TimeframeUnit.D)
            return b;

        if (baseTf.Unit != TimeframeUnit.m && baseTf.Unit != TimeframeUnit.H)
            throw new InvalidOperationException(
                $"Cannot aggregate {baseTf} to {target}");

        int count = b.Count;

        ReadOnlySpan<DateTime> dt = b.DateTime.Span;
        ReadOnlySpan<decimal>  op = b.Open.Span;
        ReadOnlySpan<decimal>  hi = b.High.Span;
        ReadOnlySpan<decimal>  lo = b.Low.Span;
        ReadOnlySpan<decimal>  cl = b.Close.Span;
        ReadOnlySpan<decimal>  vo = b.Volume.Span;
        ReadOnlySpan<decimal>  oi = b.OI.Span;

        var outDt = new List<DateTime>(64);
        var outOp = new List<decimal>(64);
        var outHi = new List<decimal>(64);
        var outLo = new List<decimal>(64);
        var outCl = new List<decimal>(64);
        var outVo = new List<decimal>(64);
        var outOi = new List<decimal>(64);

        int i = 0;
        while (i < count)
        {
            DateTime dayKey = ToIst(dt[i]).Date;
            int startI = i;
            i++;
            while (i < count && ToIst(dt[i]).Date == dayKey) i++;
            int endI = i;

            decimal fOp = op[startI];
            decimal fHi = hi[startI];
            decimal fLo = lo[startI];
            decimal fVo = vo[startI];
            for (int k = startI + 1; k < endI; k++)
            {
                decimal hk = hi[k];
                if (hk > fHi) fHi = hk;
                decimal lk = lo[k];
                if (lk < fLo) fLo = lk;
                fVo += vo[k];
            }
            decimal fCl = cl[endI - 1];
            decimal fOi = oi[endI - 1];

            outDt.Add(dt[startI]);
            outOp.Add(fOp);
            outHi.Add(fHi);
            outLo.Add(fLo);
            outCl.Add(fCl);
            outVo.Add(fVo);
            outOi.Add(fOi);
        }

        return new ColumnarBarSeries(
            b.InstrumentToken, b.Symbol, target,
            outOp.ToArray(), outHi.ToArray(), outLo.ToArray(),
            outCl.ToArray(), outVo.ToArray(), outOi.ToArray(),
            outDt.ToArray());
    }

    // ----------------------------------------------------------------
    // Weekly / Monthly aggregation (from daily)
    // ----------------------------------------------------------------
    private static IBarSeries AggregatePositional(IBarSeries b, Timeframe target)
    {
        IBarSeries daily = b.Timeframe.Unit == TimeframeUnit.D
            ? b
            : AggregateDaily(b, DailyTimeframe);

        if (daily.Count == 0)
            return new ColumnarBarSeries(
                b.InstrumentToken, b.Symbol, target,
                Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
                Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
                Array.Empty<DateTime>());

        int count = daily.Count;

        ReadOnlySpan<DateTime> dt = daily.DateTime.Span;
        ReadOnlySpan<decimal>  op = daily.Open.Span;
        ReadOnlySpan<decimal>  hi = daily.High.Span;
        ReadOnlySpan<decimal>  lo = daily.Low.Span;
        ReadOnlySpan<decimal>  cl = daily.Close.Span;
        ReadOnlySpan<decimal>  vo = daily.Volume.Span;
        ReadOnlySpan<decimal>  oi = daily.OI.Span;

        var outDt = new List<DateTime>(count / 5 + 1);
        var outOp = new List<decimal>(count / 5 + 1);
        var outHi = new List<decimal>(count / 5 + 1);
        var outLo = new List<decimal>(count / 5 + 1);
        var outCl = new List<decimal>(count / 5 + 1);
        var outVo = new List<decimal>(count / 5 + 1);
        var outOi = new List<decimal>(count / 5 + 1);

        int i = 0;
        while (i < count)
        {
            long key = GroupKey(ToIst(dt[i]), target.Unit);
            int startI = i;
            i++;
            while (i < count && GroupKey(ToIst(dt[i]), target.Unit) == key) i++;
            int endI = i;

            decimal fOp = op[startI];
            decimal fHi = hi[startI];
            decimal fLo = lo[startI];
            decimal fVo = vo[startI];
            for (int k = startI + 1; k < endI; k++)
            {
                decimal hk = hi[k];
                if (hk > fHi) fHi = hk;
                decimal lk = lo[k];
                if (lk < fLo) fLo = lk;
                fVo += vo[k];
            }
            decimal fCl = cl[endI - 1];
            decimal fOi = oi[endI - 1];

            outDt.Add(dt[startI]);
            outOp.Add(fOp);
            outHi.Add(fHi);
            outLo.Add(fLo);
            outCl.Add(fCl);
            outVo.Add(fVo);
            outOi.Add(fOi);
        }

        return new ColumnarBarSeries(
            b.InstrumentToken, b.Symbol, target,
            outOp.ToArray(), outHi.ToArray(), outLo.ToArray(),
            outCl.ToArray(), outVo.ToArray(), outOi.ToArray(),
            outDt.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GroupKey(DateTime istDate, TimeframeUnit unit) => unit switch
    {
        TimeframeUnit.W => WeekKey(istDate),
        TimeframeUnit.M => (long)istDate.Year * 100 + istDate.Month,
        _ => throw new InvalidOperationException($"Unsupported positional unit: {unit}")
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long WeekKey(DateTime istDate)
    {
        var y = System.Globalization.ISOWeek.GetYear(istDate);
        var w = System.Globalization.ISOWeek.GetWeekOfYear(istDate);
        return (long)y * 100 + w;
    }

    private static DateTime ToIst(DateTime utc) => utc.AddMinutes(330);
}
