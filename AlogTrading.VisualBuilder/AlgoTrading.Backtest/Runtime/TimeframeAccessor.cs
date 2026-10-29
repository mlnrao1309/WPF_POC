using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Resolves a Timeframe to an AlignedSeries. Caches per run.
/// The base series is provided at construction; higher timeframes come from
/// the TimeframeCache.
/// </summary>
public sealed class TimeframeAccessor
{
    private readonly uint _token;
    private readonly TimeframeCache _cache;
    private readonly Dictionary<Timeframe, AlignedSeries> _resolved = new();

    public IBarSeries BaseSeries { get; }
    public int BaseBarCount => BaseSeries.Count;

    public TimeframeAccessor(uint token, IBarSeries baseSeries, TimeframeCache cache)
    {
        ArgumentNullException.ThrowIfNull(baseSeries);
        ArgumentNullException.ThrowIfNull(cache);
        _token = token;
        BaseSeries = baseSeries;
        _cache = cache;
    }

    internal TimeframeAccessor ForTimeframe(Timeframe tf)
        => new(_token, Resolve(tf).Bars, _cache);

    public AlignedSeries Resolve(Timeframe tf)
    {
        if (_resolved.TryGetValue(tf, out var cached)) return cached;

        IBarSeries tfBars = tf.Equals(BaseSeries.Timeframe)
            ? BaseSeries
            : _cache.Get(_token, tf);

        var map = BuildBaseToTfMap(BaseSeries, tfBars);
        var aligned = new AlignedSeries { Bars = tfBars, Timeframe = tf, BaseToTfIndex = map };
        _resolved[tf] = aligned;
        return aligned;
    }

    private static int[] BuildBaseToTfMap(IBarSeries baseSeries, IBarSeries tfBars)
    {
        int n = baseSeries.Count;
        int m = tfBars.Count;
        var map = new int[n];
        if (m == 0) { for (int i = 0; i < n; i++) map[i] = -1; return map; }

        int j = -1;
        for (int i = 0; i < n; i++)
        {
            DateTime baseEnd = BarEnd(baseSeries, i);

            while (j + 1 < m)
            {
                int k = j + 1;
                DateTime tfEnd = BarEnd(tfBars, k);

                if (tfEnd <= baseEnd) j = k;
                else break;
            }
            map[i] = j;
        }
        return map;
    }

    public static DateTime BarEnd(IBarSeries series, int index)
    {
        if (series is SqlBarSeries sql && sql.CloseTime.Length == series.Count)
            return sql.CloseTime.Span[index];
        var open = series.DateTime.Span[index];
        var tf = series.Timeframe;
        if (tf.Unit is TimeframeUnit.m or TimeframeUnit.H)
            return open.AddMinutes(tf.ToMinutes());
        var localDate = open.AddMinutes(330).Date;
        var end = tf.Unit switch
        {
            TimeframeUnit.D => localDate.AddDays(1),
            TimeframeUnit.W => localDate.AddDays(7 - ((int)localDate.DayOfWeek + 6) % 7),
            TimeframeUnit.M => new DateTime(localDate.Year, localDate.Month, 1).AddMonths(1),
            _ => throw new InvalidOperationException($"Unsupported timeframe '{tf}'")
        };
        return end.AddMinutes(-330);
    }
}
