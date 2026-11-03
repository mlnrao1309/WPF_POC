using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public static class WalkForward
{
    public static WalkForwardResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions baseOptions,
        WalkForwardConfig config,
        int maxCombinations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(baseOptions);
        ArgumentNullException.ThrowIfNull(config);

        if (!double.IsFinite(config.FinalHoldoutRatio) || config.FinalHoldoutRatio < 0 || config.FinalHoldoutRatio >= 1)
            throw new ArgumentOutOfRangeException(nameof(config.FinalHoldoutRatio), "Holdout ratio must be >= 0 and < 1.");
        if (!double.IsFinite(config.InSampleRatio) || config.InSampleRatio <= 0 || config.InSampleRatio >= 1)
            throw new ArgumentOutOfRangeException(nameof(config.InSampleRatio), "Training ratio must be > 0 and < 1.");
        if (config.WindowCount < 1) throw new ArgumentOutOfRangeException(nameof(config.WindowCount));
        if (config.WarmupDays < 0) throw new ArgumentOutOfRangeException(nameof(config.WarmupDays));
        if (!Enum.IsDefined(config.BoundaryPolicy)) throw new ArgumentOutOfRangeException(nameof(config.BoundaryPolicy));
        if (series.Count < 2) return EmptyResult(config);

        if (config.BoundaryPolicy == BoundaryPolicy.CarryToNextWindow)
            throw new NotSupportedException("CarryToNextWindow is not yet implemented. Please use FlatAtBoundary.");

        var windows = new List<WalkForwardWindow>(config.WindowCount);
        var oosTrades = new List<Trade>();
        var oosEquity = new List<EquityPoint>();

        DateTime from = series.From;
        DateTime to = series.To;

        long totalTicks = (to - from).Ticks;
        long holdoutTicks = (long)(totalTicks * config.FinalHoldoutRatio);

        // FIX: Only subtract a tick when an actual holdout is being reserved
        DateTime holdoutStart;
        DateTime tilingEnd;
        if (config.FinalHoldoutRatio > 0 && holdoutTicks > 0)
        {
            holdoutStart = to.AddTicks(-holdoutTicks);
            tilingEnd = holdoutStart.AddTicks(-1);
        }
        else
        {
            holdoutStart = to;   // no holdout
            tilingEnd = to;
        }

        long tilingTicks = (tilingEnd - from).Ticks;
        if (tilingTicks <= 0) return EmptyResult(config);

        decimal lastEquity = baseOptions.InitialCapital;
        var bindings = ParameterSweep.CreateBindingsFromSpecs(rule.Parameters ?? new List<ParameterSpec>());

        for (int k = 0; k < config.WindowCount; k++)
        {
            ct.ThrowIfCancellationRequested();

            long wStartTicks = (long)((decimal)tilingTicks * k / config.WindowCount);
            long wEndTicks = (long)((decimal)tilingTicks * (k + 1) / config.WindowCount);
            DateTime wStart = from.AddTicks(wStartTicks);
            DateTime wEnd = from.AddTicks(wEndTicks);

            long windowTicks = (wEnd - wStart).Ticks;
            long isTicks = (long)((decimal)windowTicks * (decimal)config.InSampleRatio);
            if (isTicks < 1) isTicks = 1;
            if (isTicks >= windowTicks) isTicks = windowTicks - 1;
            DateTime isEnd = wStart.AddTicks(isTicks);
            DateTime oosStart = isEnd.AddTicks(1);

            DateTime isWarmupFrom = wStart.AddDays(-config.WarmupDays);
            DateTime oosWarmupFrom = oosStart.AddDays(-config.WarmupDays);

            if (isWarmupFrom < from) isWarmupFrom = from;
            if (oosWarmupFrom < from) oosWarmupFrom = from;

            var isSlice = new SliceBarSeries(series, isWarmupFrom, isEnd);
            var oosSlice = new SliceBarSeries(series, oosWarmupFrom, wEnd);

            if (isSlice.Count < 2 || oosSlice.Count < 2)
            {
                windows.Add(EmptyWindow(k, isWarmupFrom, wStart, isEnd, oosWarmupFrom, oosStart, wEnd));
                continue;
            }

            var isOptions = new BacktestEngineOptions
            {
                EnableTracing = false,
                InitialCapital = baseOptions.InitialCapital,
                LossBasis = baseOptions.LossBasis,
                RiskFreeRatePercent = baseOptions.RiskFreeRatePercent,
                Quantity = baseOptions.Quantity,
                MaxParallelism = baseOptions.MaxParallelism,
                CostsJson = baseOptions.CostsJson,
                SlippageJson = baseOptions.SlippageJson,
                MarginsJson = baseOptions.MarginsJson,
                DataSource = baseOptions.DataSource,
                TradingStartTime = wStart,
                PreparedSeries = baseOptions.PreparedSeries,
                DailySeries = baseOptions.DailySeries
            };

            SweepResult sweep;
            try
            {
                sweep = ParameterSweep.Run(rule, isSlice, isOptions, SweepCriterion.Sharpe, bindings, validityCheck: null, maxCombinations, ct);
            }
            catch (ArgumentException)
            {
                windows.Add(EmptyWindow(k, isWarmupFrom, wStart, isEnd, oosWarmupFrom, oosStart, wEnd));
                continue;
            }

            if (!sweep.Best.IsValid)
            {
                windows.Add(EmptyWindow(k, isWarmupFrom, wStart, isEnd, oosWarmupFrom, oosStart, wEnd));
                continue;
            }

            var bestParams = sweep.Best.Parameters;
            var oosRule = RuleDefinitionJson.Deserialize(RuleDefinitionJson.Serialize(rule));
            ApplyParameters(oosRule, bestParams);

            var oosOptions = new BacktestEngineOptions
            {
                EnableTracing = false,
                InitialCapital = lastEquity,
                LossBasis = baseOptions.LossBasis,
                RiskFreeRatePercent = baseOptions.RiskFreeRatePercent,
                Quantity = baseOptions.Quantity,
                MaxParallelism = baseOptions.MaxParallelism,
                CostsJson = baseOptions.CostsJson,
                SlippageJson = baseOptions.SlippageJson,
                MarginsJson = baseOptions.MarginsJson,
                DataSource = baseOptions.DataSource,
                TradingStartTime = oosStart,
                PreparedSeries = baseOptions.PreparedSeries,
                DailySeries = baseOptions.DailySeries
            };

            var engine = new BacktestEngine(oosOptions);
            var oosRun = engine.Run(oosRule, oosSlice, ct);

            foreach (var t in oosRun.Trades) oosTrades.Add(t);
            if (oosRun.EquityCurve.Count > 0)
            {
                decimal baseEq = oosRun.EquityCurve[0].Equity;
                foreach (var p in oosRun.EquityCurve)
                {
                    if (p.DateTime < oosStart) continue;
                    oosEquity.Add(new EquityPoint { DateTime = p.DateTime, Equity = lastEquity + (p.Equity - baseEq) });
                }
                if (oosEquity.Count > 0) lastEquity = oosEquity[^1].Equity;
            }

            decimal isSharpe = sweep.Best.Metrics.Sharpe;
            decimal oosSharpe = oosRun.Metrics.Sharpe;
            decimal degradation = isSharpe > 0m ? oosSharpe / isSharpe : 0m;

            var rejected = sweep.Ranked
                .Where(x => !x.IsValid || BuildParamString(x.Parameters) != BuildParamString(bestParams))
                .Select(x => BuildParamString(x.Parameters))
                .ToList();

            windows.Add(new WalkForwardWindow
            {
                WindowIndex = k,
                ISWarmupFrom = isWarmupFrom,
                ISFrom = wStart,
                ISTo = isEnd,
                OOSWarmupFrom = oosWarmupFrom,
                OOSFrom = oosStart,
                OOSTo = wEnd,
                BestParameters = bestParams,
                ISMetrics = sweep.Best.Metrics,
                OOSMetrics = oosRun.Metrics,
                DegradationRatio = degradation,
                RejectedTrials = rejected,
                TestWasFrozen = true
            });
        }

        BacktestMetrics? holdoutMetrics = null;
        DateTime? holdoutFrom = null, holdoutTo = null;

        if (config.FinalHoldoutRatio > 0 && holdoutStart < to)
        {
            holdoutFrom = holdoutStart;
            holdoutTo = to;

            var lastParams = windows.LastOrDefault(w => w.BestParameters.Count > 0)?.BestParameters ?? new Dictionary<string, decimal>();
            var holdoutRule = RuleDefinitionJson.Deserialize(RuleDefinitionJson.Serialize(rule));
            ApplyParameters(holdoutRule, lastParams);

            var holdoutWarmupFrom = holdoutStart.AddDays(-config.WarmupDays);
            if (holdoutWarmupFrom < from) holdoutWarmupFrom = from;

            var holdoutSlice = new SliceBarSeries(series, holdoutWarmupFrom, to);

            var holdoutOptions = new BacktestEngineOptions
            {
                EnableTracing = false,
                InitialCapital = lastEquity,
                LossBasis = baseOptions.LossBasis,
                RiskFreeRatePercent = baseOptions.RiskFreeRatePercent,
                Quantity = baseOptions.Quantity,
                MaxParallelism = baseOptions.MaxParallelism,
                CostsJson = baseOptions.CostsJson,
                SlippageJson = baseOptions.SlippageJson,
                MarginsJson = baseOptions.MarginsJson,
                DataSource = baseOptions.DataSource,
                TradingStartTime = holdoutStart,
                PreparedSeries = baseOptions.PreparedSeries,
                DailySeries = baseOptions.DailySeries
            };

            var holdoutRun = new BacktestEngine(holdoutOptions).Run(holdoutRule, holdoutSlice, ct);
            holdoutMetrics = holdoutRun.Metrics;
        }

        var aggMetrics = MetricsCalculator.Compute(oosTrades, oosEquity, baseOptions.InitialCapital, baseOptions.RiskFreeRatePercent);

        int positiveSharpe = 0;
        foreach (var w in windows) if (w.OOSMetrics.Sharpe > 0m) positiveSharpe++;
        double consistency = config.WindowCount > 0 ? (double)positiveSharpe / config.WindowCount : 0.0;

        return new WalkForwardResult
        {
            Windows = windows,
            AggregateOOSMetrics = aggMetrics,
            AggregateOOSSharpe = aggMetrics.Sharpe,
            AggregateOOSProfitFactor = aggMetrics.ProfitFactor,
            AggregateOOSMaxDrawdownPct = aggMetrics.MaxDrawdownPct,
            ConsistencyScore = consistency,
            HoldoutFrom = holdoutFrom,
            HoldoutTo = holdoutTo,
            HoldoutMetrics = holdoutMetrics
        };
    }

    private static string BuildParamString(Dictionary<string, decimal> p)
        => string.Join(",", p.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));

    private static void ApplyParameters(RuleDefinition rule, IReadOnlyDictionary<string, decimal> p)
    {
        if (rule.Parameters is null) return;
        foreach (var spec in rule.Parameters)
            if (p.TryGetValue(spec.Name, out var v))
                spec.Default = v;
    }

    private static WalkForwardWindow EmptyWindow(int k, DateTime isWarmup, DateTime isFrom, DateTime isTo, DateTime oosWarmup, DateTime oosFrom, DateTime oosTo)
    {
        return new WalkForwardWindow
        {
            WindowIndex = k,
            ISWarmupFrom = isWarmup,
            ISFrom = isFrom,
            ISTo = isTo,
            OOSWarmupFrom = oosWarmup,
            OOSFrom = oosFrom,
            OOSTo = oosTo,
            BestParameters = new Dictionary<string, decimal>(),
            ISMetrics = new BacktestMetrics(),
            OOSMetrics = new BacktestMetrics(),
            DegradationRatio = 0m,
            TestWasFrozen = false
        };
    }

    private static WalkForwardResult EmptyResult(WalkForwardConfig config)
    {
        var windows = new List<WalkForwardWindow>(config.WindowCount);
        for (int k = 0; k < config.WindowCount; k++) windows.Add(EmptyWindow(k, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue));
        return new WalkForwardResult { Windows = windows, AggregateOOSMetrics = new BacktestMetrics(), AggregateOOSSharpe = 0m, AggregateOOSProfitFactor = 0m, AggregateOOSMaxDrawdownPct = 0m, ConsistencyScore = 0.0 };
    }
}