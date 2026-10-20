using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Internal read-only subset of an IBarSeries over an inclusive date range.
/// Used by walk-forward to slice in-memory bar series. Empty slices are
/// legal (Count == 0, all memories empty); From/To reflect the requested
/// bounds so callers always see something meaningful.
/// </summary>
internal sealed class SliceBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public DateTime From { get; }
    public DateTime To { get; }
    public int Count { get; }

    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<DateTime> DateTime { get; }

    public SliceBarSeries(IBarSeries source, DateTime fromInclusive, DateTime toInclusive)
    {
        ArgumentNullException.ThrowIfNull(source);

        InstrumentToken = source.InstrumentToken;
        Symbol = source.Symbol;
        Timeframe = source.Timeframe;

        var dtSpan = source.DateTime.Span;
        int lo = LowerBound(dtSpan, fromInclusive);
        int hi = UpperBound(dtSpan, toInclusive);

        if (lo >= hi)
        {
            Open = ReadOnlyMemory<decimal>.Empty;
            High = ReadOnlyMemory<decimal>.Empty;
            Low = ReadOnlyMemory<decimal>.Empty;
            Close = ReadOnlyMemory<decimal>.Empty;
            Volume = ReadOnlyMemory<decimal>.Empty;
            OI = ReadOnlyMemory<decimal>.Empty;
            DateTime = ReadOnlyMemory<DateTime>.Empty;
            Count = 0;
            From = fromInclusive;
            To = toInclusive;
            return;
        }

        int cnt = hi - lo;
        Open = source.Open.Slice(lo, cnt);
        High = source.High.Slice(lo, cnt);
        Low = source.Low.Slice(lo, cnt);
        Close = source.Close.Slice(lo, cnt);
        Volume = source.Volume.Slice(lo, cnt);
        OI = source.OI.Slice(lo, cnt);
        DateTime = source.DateTime.Slice(lo, cnt);
        Count = cnt;
        From = dtSpan[lo];
        To = dtSpan[hi - 1];
    }

    private static int LowerBound(ReadOnlySpan<DateTime> a, DateTime target)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (a[mid] < target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(ReadOnlySpan<DateTime> a, DateTime target)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (a[mid] <= target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}