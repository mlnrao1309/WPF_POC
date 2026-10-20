using System;
using System.Collections.Generic;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public sealed class WalkForwardWindow
{
    public int WindowIndex { get; init; }
    public DateTime ISFrom { get; init; }
    public DateTime ISTo { get; init; }
    public DateTime OOSFrom { get; init; }
    public DateTime OOSTo { get; init; }
    public IReadOnlyDictionary<string, decimal> BestParameters { get; init; } = new Dictionary<string, decimal>();
    public BacktestMetrics ISMetrics { get; init; } = new BacktestMetrics();
    public BacktestMetrics OOSMetrics { get; init; } = new BacktestMetrics();
    public decimal DegradationRatio { get; init; }
}

public sealed class WalkForwardResult
{
    public IReadOnlyList<WalkForwardWindow> Windows { get; init; } = Array.Empty<WalkForwardWindow>();
    public decimal AggregateOOSSharpe { get; init; }
    public decimal AggregateOOSProfitFactor { get; init; }
    public decimal AggregateOOSMaxDrawdownPct { get; init; }
    public decimal ConsistencyScore { get; init; }
}

public static class WalkForward
{
    public static WalkForwardResult Run(
        RuleDefinition rule, uint token, IBarSeries baseSeries, BacktestConfig cfg,
        ITransactionCostModel cost, ISlippageModel slip, IMarginModel margin)
    {
        int totalBars = baseSeries.Count;
        int windows = cfg.WalkForward.WindowCount;
        if (windows < 1) throw new ArgumentException("WindowCount must be >= 1.");

        int barsPerWindow = totalBars / windows;
        if (barsPerWindow < 2) throw new InvalidOperationException("Not enough bars for requested window count.");

        var dt = baseSeries.DateTime.Span;
        var results = new List<WalkForwardWindow>();

        for (int w = 0; w < windows; w++)
        {
            int start = w * barsPerWindow;
            int end = (w == windows - 1) ? totalBars : start + barsPerWindow;
            int span = end - start;
            int isCount = (int)(span * cfg.WalkForward.InSampleRatio);
            if (isCount < 1 || isCount >= span) continue;

            int isEnd = start + isCount;
            int oosStart = isEnd, oosEnd = end;

            var isSlice  = Slice(baseSeries, start, isEnd);
            var oosSlice = Slice(baseSeries, oosStart, oosEnd);

            var sweep = ParameterSweep.Run(rule, token, isSlice, cfg, cost, slip, margin);
            var best = sweep.Best?.Parameters ?? new Dictionary<string, decimal>();

            var tuned = ApplyParams(rule, best);

            var isCache = new TimeframeCache(isSlice);
            var isEngine = new BacktestEngine(tuned, token, isSlice, isCache, cfg, cost, slip, margin);
            var isRes = isEngine.Run();

            var oosCache = new TimeframeCache(oosSlice);
            var oosEngine = new BacktestEngine(tuned, token, oosSlice, oosCache, cfg, cost, slip, margin);
            var oosRes = oosEngine.Run();

            decimal deg = isRes.Metrics.SharpeRatio != 0m
                ? (isRes.Metrics.SharpeRatio - oosRes.Metrics.SharpeRatio) / isRes.Metrics.SharpeRatio
                : 0m;

            results.Add(new WalkForwardWindow
            {
                WindowIndex = w,
                ISFrom = dt[start], ISTo = dt[isEnd - 1],
                OOSFrom = dt[oosStart], OOSTo = dt[oosEnd - 1],
                BestParameters = best,
                ISMetrics = isRes.Metrics,
                OOSMetrics = oosRes.Metrics,
                DegradationRatio = deg,
            });
        }

        if (results.Count == 0) return new WalkForwardResult();

        decimal sumS = 0m, sumP = 0m, sumD = 0m;
        int pos = 0;
        for (int i = 0; i < results.Count; i++)
        {
            sumS += results[i].OOSMetrics.SharpeRatio;
            sumP += results[i].OOSMetrics.ProfitFactor;
            sumD += results[i].OOSMetrics.MaxDrawdownPct;
            if (results[i].OOSMetrics.TotalNetPnL > 0m) pos++;
        }
        int n = results.Count;
        return new WalkForwardResult
        {
            Windows = results,
            AggregateOOSSharpe = sumS / n,
            AggregateOOSProfitFactor = sumP / n,
            AggregateOOSMaxDrawdownPct = sumD / n,
            ConsistencyScore = (decimal)pos / n,
        };
    }

    private static IBarSeries Slice(IBarSeries s, int from, int len)
        => new SlicedBarSeries(s.InstrumentToken, s.Symbol, s.Timeframe,
            s.DateTime.Slice(from, len),
            s.Open.Slice(from, len), s.High.Slice(from, len), s.Low.Slice(from, len),
            s.Close.Slice(from, len), s.Volume.Slice(from, len), s.OI.Slice(from, len));

    private static RuleDefinition ApplyParams(RuleDefinition r, IReadOnlyDictionary<string, decimal> overrides)
    {
        string json = RuleDefinitionJson.Serialize(r);
        var clone = RuleDefinitionJson.Deserialize(json);
        if (clone.Parameters is not null)
            for (int i = 0; i < clone.Parameters.Count; i++)
                if (overrides.TryGetValue(clone.Parameters[i].Name, out var v))
                    clone.Parameters[i].Default = v;
        return clone;
    }
}

internal sealed class SlicedBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public DateTime From => DateTime.Length > 0 ? DateTime.Span[0] : default;
    public DateTime To   => DateTime.Length > 0 ? DateTime.Span[DateTime.Length - 1] : default;
    public int Count => DateTime.Length;
    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<DateTime> DateTime { get; }

    public SlicedBarSeries(uint t, string s, Timeframe tf,
        ReadOnlyMemory<DateTime> dt, ReadOnlyMemory<decimal> o, ReadOnlyMemory<decimal> h,
        ReadOnlyMemory<decimal> l, ReadOnlyMemory<decimal> c, ReadOnlyMemory<decimal> v, ReadOnlyMemory<decimal> oi)
    {
        InstrumentToken = t; Symbol = s; Timeframe = tf;
        DateTime = dt; Open = o; High = h; Low = l; Close = c; Volume = v; OI = oi;
    }
}