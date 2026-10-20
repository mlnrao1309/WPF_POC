using System;
using System.Collections.Generic;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal sealed class TimeframeAligner
{
    private readonly Timeframe _baseTf;
    private readonly Dictionary<Timeframe, int[]> _maps = new();
    private readonly Dictionary<Timeframe, IBarSeries> _series = new();

    public Timeframe BaseTimeframe => _baseTf;

    public TimeframeAligner(IBarSeries baseSeries, TimeframeCache cache, uint token, IEnumerable<Timeframe> referenced)
    {
        _baseTf = baseSeries.Timeframe;
        _series[_baseTf] = baseSeries;

        var identity = new int[baseSeries.Count];
        for (int i = 0; i < identity.Length; i++) identity[i] = i;
        _maps[_baseTf] = identity;

        foreach (var tf in referenced)
        {
            if (tf == _baseTf) continue;
            if (_maps.ContainsKey(tf)) continue;
            IBarSeries agg = cache.Get(token, tf);
            _series[tf] = agg;
            _maps[tf] = BuildMap(baseSeries, agg);
        }
    }

    public IBarSeries Series(Timeframe tf)
    {
        if (_series.TryGetValue(tf, out var s)) return s;
        throw new InvalidOperationException($"Timeframe {tf} not prepared.");
    }

    public int[] GetMap(Timeframe tf)
    {
        if (_maps.TryGetValue(tf, out var m)) return m;
        throw new InvalidOperationException($"Timeframe {tf} not prepared.");
    }

    public bool HasTimeframe(Timeframe tf) => _maps.ContainsKey(tf);

    private static int[] BuildMap(IBarSeries baseS, IBarSeries agg)
    {
        int n = baseS.Count, m = agg.Count;
        var map = new int[n];
        if (m == 0) { for (int i = 0; i < n; i++) map[i] = -1; return map; }

        var bdt = baseS.DateTime.Span;
        var adt = agg.DateTime.Span;
        var ends = new DateTime[m];
        for (int j = 0; j < m; j++)
        {
            if (j + 1 < m) ends[j] = adt[j + 1];
            else if (j > 0) ends[j] = adt[j] + (adt[j] - adt[j - 1]);
            else ends[j] = adt[j] + TimeSpan.FromMinutes(1);
        }
        int jj = -1;
        for (int i = 0; i < n; i++)
        {
            while (jj + 1 < m && ends[jj + 1] <= bdt[i]) jj++;
            map[i] = jj;
        }
        return map;
    }
}