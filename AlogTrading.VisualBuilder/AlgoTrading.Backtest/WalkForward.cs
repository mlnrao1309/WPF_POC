using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

/// <summary>
/// Walk-forward analysis (Section 5.16). The full date range is tiled by
/// WindowCount disjoint windows. Within each window, the first
/// InSampleRatio fraction is used for a parameter sweep; the best
/// combination is then re-run on the OOS slice. Aggregate metrics come
/// from the concatenated OOS trades and a rescaled OOS equity curve.
/// </summary>
public static class WalkForward
{
    public static WalkForwardResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions options,
        WalkForwardConfig config,
        int maxCombinations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);
        if (config.WindowCount < 1)
            throw new ArgumentOutOfRangeException(nameof(config),
                "WindowCount must be >= 1.");
        if (config.InSampleRatio <= 0.0 || config.InSampleRatio >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(config),
                "InSampleRatio must be strictly between 0 and 1.");
        if (maxCombinations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations),
                "maxCombinations must be >= 1.");

        if (series.Count < 2)
            return EmptyResult(config);

        var windows = new List<WalkForwardWindow>(config.WindowCount);
        var oosTrades = new List<Trade>();
        var oosEquity = new List<EquityPoint>();

        var engine = new BacktestEngine(options);

        DateTime from = series.From;
        DateTime to = series.To;
        long totalTicks = (to - from).Ticks;
        if (totalTicks <= 0)
            return EmptyResult(config);

        decimal lastEquity = options.InitialCapital;

        for (int k = 0; k < config.WindowCount; k++)
        {
            ct.ThrowIfCancellationRequested();

            long wStartTicks = (long)((decimal)totalTicks * k / config.WindowCount);
            long wEndTicks   = (long)((decimal)totalTicks * (k + 1) / config.WindowCount);
            DateTime wStart = from.AddTicks(wStartTicks);
            DateTime wEnd   = from.AddTicks(wEndTicks);

            long windowTicks = (wEnd - wStart).Ticks;
            long isTicks = (long)((decimal)windowTicks * (decimal)config.InSampleRatio);
            if (isTicks < 1) isTicks = 1;
            if (isTicks >= windowTicks) isTicks = windowTicks - 1;
            DateTime isEnd = wStart.AddTicks(isTicks);
            DateTime oosStart = isEnd.AddTicks(1);

            var isSlice = new SliceBarSeries(series, wStart, isEnd);
            var oosSlice = new SliceBarSeries(series, oosStart, wEnd);

            if (isSlice.Count < 2 || oosSlice.Count < 2)
            {
                windows.Add(EmptyWindow(k, isSlice, oosSlice));
                continue;
            }

            SweepResult sweep;
            try
            {
                sweep = ParameterSweep.Run(
                    rule, isSlice, options, SweepCriterion.Sharpe, maxCombinations, ct);
            }
            catch (ArgumentException)
            {
                windows.Add(EmptyWindow(k, isSlice, oosSlice));
                continue;
            }

            var bestParams = sweep.Best.Parameters;

            var oosRule = RuleDefinitionJson.Deserialize(RuleDefinitionJson.Serialize(rule));
            ApplyParameters(oosRule, bestParams);

            var oosRun = engine.Run(oosRule, oosSlice, ct);

            foreach (var t in oosRun.Trades) oosTrades.Add(t);
            if (oosRun.EquityCurve.Count > 0)
            {
                decimal baseEq = oosRun.EquityCurve[0].Equity;
                foreach (var p in oosRun.EquityCurve)
                {
                    oosEquity.Add(new EquityPoint
                    {
                        DateTime = p.DateTime,
                        Equity = lastEquity + (p.Equity - baseEq)
                    });
                }
                lastEquity = oosEquity[oosEquity.Count - 1].Equity;
            }

            decimal isSharpe = sweep.Best.Metrics.Sharpe;
            decimal oosSharpe = oosRun.Metrics.Sharpe;
            decimal degradation = isSharpe > 0m ? oosSharpe / isSharpe : 0m;

            windows.Add(new WalkForwardWindow
            {
                WindowIndex = k,
                ISFrom = isSlice.From,
                ISTo = isSlice.To,
                OOSFrom = oosSlice.From,
                OOSTo = oosSlice.To,
                BestParameters = bestParams,
                ISMetrics = sweep.Best.Metrics,
                OOSMetrics = oosRun.Metrics,
                DegradationRatio = degradation
            });
        }

        var aggMetrics = MetricsCalculator.Compute(
            oosTrades, oosEquity, options.InitialCapital, options.RiskFreeRatePercent);

        int positiveSharpe = 0;
        foreach (var w in windows)
            if (w.OOSMetrics.Sharpe > 0m) positiveSharpe++;

        double consistency = config.WindowCount > 0
            ? (double)positiveSharpe / config.WindowCount
            : 0.0;

        return new WalkForwardResult
        {
            Windows = windows,
            AggregateOOSMetrics = aggMetrics,
            AggregateOOSSharpe = aggMetrics.Sharpe,
            AggregateOOSProfitFactor = aggMetrics.ProfitFactor,
            AggregateOOSMaxDrawdownPct = aggMetrics.MaxDrawdownPct,
            ConsistencyScore = consistency
        };
    }

    private static void ApplyParameters(
        RuleDefinition rule, IReadOnlyDictionary<string, decimal> p)
    {
        if (rule.Parameters is null) return;
        foreach (var spec in rule.Parameters)
            if (p.TryGetValue(spec.Name, out var v))
                spec.Default = v;
    }

    private static WalkForwardWindow EmptyWindow(int k, IBarSeries isSlice, IBarSeries oosSlice)
    {
        return new WalkForwardWindow
        {
            WindowIndex = k,
            ISFrom = isSlice.From,
            ISTo = isSlice.To,
            OOSFrom = oosSlice.From,
            OOSTo = oosSlice.To,
            BestParameters = new Dictionary<string, decimal>(),
            ISMetrics = new BacktestMetrics(),
            OOSMetrics = new BacktestMetrics(),
            DegradationRatio = 0m
        };
    }

    private static WalkForwardResult EmptyResult(WalkForwardConfig config)
    {
        var windows = new List<WalkForwardWindow>(config.WindowCount);
        for (int k = 0; k < config.WindowCount; k++)
        {
            windows.Add(new WalkForwardWindow
            {
                WindowIndex = k,
                BestParameters = new Dictionary<string, decimal>(),
                ISMetrics = new BacktestMetrics(),
                OOSMetrics = new BacktestMetrics(),
                DegradationRatio = 0m
            });
        }
        return new WalkForwardResult
        {
            Windows = windows,
            AggregateOOSMetrics = new BacktestMetrics(),
            AggregateOOSSharpe = 0m,
            AggregateOOSProfitFactor = 0m,
            AggregateOOSMaxDrawdownPct = 0m,
            ConsistencyScore = 0.0
        };
    }
}