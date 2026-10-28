using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Configs;
using AlgoTrading.Backtest.Contracts;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Single-instrument, single-position backtest engine (Section 5).
/// Signals are evaluated on completed bar i and queued for execution at
/// bar i+1's open (Section 5.3). Intrabar SL/TP execute within bar i against
/// the bar's High/Low. Per Section 5.3's "continue to next bar" clause, no
/// signal evaluation happens on a bar that saw an intrabar exit.
/// Equity is marked-to-market at each bar's close.
/// </summary>
public sealed class BacktestEngine
{
    private readonly BacktestEngineOptions _options;
    private readonly CostsConfig _costs;
    private readonly SlippageConfig _slippage;
    private readonly MarginConfig _margins;
    private readonly ITransactionCostModel _costModel;
    private readonly ISlippageModel _slippageModel;
    private readonly IMarginModel _marginModel;

    public BacktestEngine(BacktestEngineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.InitialCapital <= 0m || options.DailyLossLimitPercent <= 0m || options.MonthlyLossLimitPercent <= 0m)
            throw new ArgumentOutOfRangeException(nameof(options), "Capital and loss limits must be positive.");
        if (!Enum.IsDefined(options.LossBasis))
            throw new ArgumentOutOfRangeException(nameof(options.LossBasis));
        _costs = CostsConfig.Parse(options.CostsJson);
        _slippage = SlippageConfig.Parse(options.SlippageJson);
        _margins = MarginConfig.Parse(options.MarginsJson);

        _costModel = new IndianEquityCostModel(_costs);
        _slippageModel = BuildSlippageModel(_slippage);
        _marginModel = new EquityMarginModel(_margins);
    }

    private static ISlippageModel BuildSlippageModel(SlippageConfig cfg)
    {
        var model = cfg.Equity.Model ?? "Spread";
        if (string.Equals(model, "Fixed", StringComparison.OrdinalIgnoreCase))
            return new FixedSlippageModel(cfg.Equity.FixedAmount);
        return new SpreadSlippageModel(cfg.Equity.SpreadPercent);
    }

    public BacktestResult Run(RuleDefinition rule, IBarSeries baseSeries,
                              CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(baseSeries);

        if (baseSeries.Count < 2)
            return EmptyResult(rule, baseSeries);

        var cache = new TimeframeCache(baseSeries, _options.DailySeries, _options.PreparedSeries);
        var accessor = new TimeframeAccessor(baseSeries.InstrumentToken, baseSeries, cache);
        var indicators = new IndicatorProvider(accessor);

        var parameters = BuildParameters(rule);
        var expr = new ExpressionEvaluator(accessor, indicators, parameters);
        var cond = new ConditionEvaluator(expr);

        var defaultTf = rule.EvaluationTimeframe;
        var slRuleGroup = rule.StopLoss?.Mode == StopLossMode.Rule
            ? NamedGroupFinder.Find(rule, rule.StopLoss.GroupName ?? "") : null;
        var tpRuleGroup = rule.Target?.Mode == TargetMode.Rule
            ? NamedGroupFinder.Find(rule, rule.Target.GroupName ?? "") : null;

        var trades = new List<Trade>();
        var equity = new List<EquityPoint>(baseSeries.Count);

        var position = new PositionState();
        int tradeId = 0;
        decimal equityBase = _options.InitialCapital;

        bool pendingEntry = false;
        ExitReason? pendingExitReason = null;

        var closes = baseSeries.Close.Span;
        var highs = baseSeries.High.Span;
        var lows = baseSeries.Low.Span;
        var opens = baseSeries.Open.Span;
        var dts = baseSeries.DateTime.Span;

        int barCount = baseSeries.Count;

        // Bar 0 close equity = initial capital (nothing has happened yet).
        equity.Add(new EquityPoint { DateTime = dts[0], Equity = equityBase });

        for (int i = 1; i < barCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Execute a pending exit queued at bar i-1 at bar i's open.
            if (pendingExitReason.HasValue && position.IsOpen)
            {
                var fill = ApplyExitSlippage(position.Direction, opens[i]);
                var trade = CloseTrade(tradeId++, rule, position, fill,
                                       dts[i], i, pendingExitReason.Value);
                trades.Add(trade);
                equityBase += trade.NetPnL;
                position.Reset();
                pendingExitReason = null;
            }

            // 2. Execute a pending entry queued at bar i-1 at bar i's open.
            if (pendingEntry && !position.IsOpen)
            {
                var fill = ApplyEntrySlippage(rule.Direction, opens[i]);
                var qty = _options.Quantity;
                var requiredMargin = _marginModel.Compute(fill, qty, rule.ExecutionMode);
                var entryCosts = _costModel.Compute(new TradeExecutionContext
                {
                    EntryPrice = fill, ExitPrice = 0m, Quantity = qty,
                    Direction = rule.Direction, Mode = rule.ExecutionMode, Scope = TradeCostScope.Entry
                }).Total;
                if (requiredMargin + entryCosts <= equityBase)
                {
                    OpenPosition(position, rule, fill, dts[i], i, qty, entryCosts,
                                 slRuleGroup, tpRuleGroup);
                }
                pendingEntry = false;
            }

            // 3. Intrabar SL/TP against bar i's High/Low.
            bool intrabarExitHappened = false;
            if (position.IsOpen)
            {
                position.UpdateExcursions(highs[i], lows[i]);
                var (triggered, exitPrice, reason) = CheckIntrabarExit(position, highs[i], lows[i]);
                if (triggered)
                {
                    var trade = CloseTrade(tradeId++, rule, position, exitPrice,
                                           dts[i], i, reason);
                    trades.Add(trade);
                    equityBase += trade.NetPnL;
                    position.Reset();
                    intrabarExitHappened = true;
                }
            }

            // Spec 5.3: "continue to next bar" after an intrabar close.
            // No signal evaluation, no entry queueing on this bar.
            if (intrabarExitHappened)
            {
                pendingExitReason = null;
                pendingEntry = false;
            }
            else
            {
                // 4. Evaluate signals on bar i's completed close.
                //    A disabled root section is treated as "not participating":
                //      eligibility -> no gate (always eligible)
                //      entry / protection / exit / fullExit -> never fires
                bool eligibility = rule.Eligibility is null
                    || !rule.Eligibility.Enabled
                    || cond.EvaluateGroup(rule.Eligibility, defaultTf, i);

                bool entry       = rule.Entry is not null
                    && rule.Entry.Enabled
                    && cond.EvaluateGroup(rule.Entry, defaultTf, i);

                bool protection  = rule.Protection is not null
                    && rule.Protection.Enabled
                    && cond.EvaluateGroup(rule.Protection, defaultTf, i);

                bool exit        = rule.Exit is not null
                    && rule.Exit.Enabled
                    && cond.EvaluateGroup(rule.Exit, defaultTf, i);

                bool fullExit    = rule.FullExit is not null
                    && rule.FullExit.Enabled
                    && cond.EvaluateGroup(rule.FullExit, defaultTf, i);

                bool slRule = slRuleGroup is not null
                    && cond.EvaluateGroup(slRuleGroup, defaultTf, i);
                bool tpRule = tpRuleGroup is not null
                    && cond.EvaluateGroup(tpRuleGroup, defaultTf, i);

                _options.SignalObserver?.Invoke(new SignalObservation(
                    DateTime.SpecifyKind(dts[i], DateTimeKind.Utc),
                    DateTime.SpecifyKind(TimeframeAccessor.BarEnd(baseSeries, i), DateTimeKind.Utc),
                    eligibility, entry, protection, exit, fullExit, slRule, tpRule));

                // 5. Queue signal-based exits for bar i+1's open.
                if (position.IsOpen && i + 1 < barCount &&
                    (fullExit || protection || exit || slRule || tpRule))
                {
                    ExitReason reason;
                    if (fullExit)        reason = ExitReason.FullExit;
                    else if (protection) reason = ExitReason.Protection;
                    else if (slRule)     reason = ExitReason.StopLoss;
                    else if (tpRule)     reason = ExitReason.Target;
                    else                 reason = ExitReason.ExitCondition;
                    pendingExitReason = reason;
                }

                // 6. Queue signal-based entries for bar i+1's open.
                if (!position.IsOpen && !pendingExitReason.HasValue
                    && eligibility && entry && i + 1 < barCount)
                {
                    pendingEntry = true;
                }
            }

            // 7. Record equity at bar i's close, marking the open position.
            decimal eq = equityBase;
            if (position.IsOpen)
            {
                decimal unrealized = position.Direction == Direction.Long
                    ? (closes[i] - position.EntryPrice) * position.Quantity
                    : (position.EntryPrice - closes[i]) * position.Quantity;
                eq += unrealized - position.EntryCosts;
            }
            equity.Add(new EquityPoint { DateTime = dts[i], Equity = eq });
        }

        // End-of-data: force-close any remaining position at the last bar's close.
        if (position.IsOpen)
        {
            int last = barCount - 1;
            var fill = ApplyExitSlippage(position.Direction, closes[last]);
            var trade = CloseTrade(tradeId++, rule, position, fill,
                                   dts[last], last, ExitReason.EndOfData);
            trades.Add(trade);
            equityBase += trade.NetPnL;
            position.Reset();

            if (equity.Count > 0)
                equity[^1] = new EquityPoint { DateTime = dts[last], Equity = equityBase };
        }

        var metrics = MetricsCalculator.Compute(trades, equity,
                                                _options.InitialCapital,
                                                _options.RiskFreeRatePercent);

        var record = new ReproducibilityRecord
        {
            RuleJson = RuleDefinitionJson.Serialize(rule),
            DataFrom = baseSeries.From,
            DataTo = baseSeries.To,
            CostConfigJson = _options.CostsJson,
            SlippageConfigJson = _options.SlippageJson,
            MarginConfigJson = _options.MarginsJson,
            EngineVersion = EngineInfo.Version,
            RunAt = DateTime.UtcNow,
            DataSource = _options.DataSource,
            InstrumentToken = baseSeries.InstrumentToken,
            BaseTimeframe = baseSeries.Timeframe.ToString(),
            RunId = _options.RunId
        };

        var result = new BacktestResult
        {
            SchemaVersion = 1,
            Reproducibility = record,
            Metrics = metrics,
            Trades = trades,
            EquityCurve = equity,
            Sweep = null,
            WalkForward = null
        };
        result.RiskAssessment = RiskAssessmentCalculator.Compute(result, _options);
        return result;
    }

    // ---------- entry / exit helpers ----------

    private decimal ApplyEntrySlippage(Direction dir, decimal refPrice)
    {
        var s = _slippageModel.Compute(refPrice);
        return dir == Direction.Long ? refPrice + s : refPrice - s;
    }

    private decimal ApplyExitSlippage(Direction dir, decimal refPrice)
    {
        var s = _slippageModel.Compute(refPrice);
        return dir == Direction.Long ? refPrice - s : refPrice + s;
    }

    private void OpenPosition(PositionState p, RuleDefinition rule, decimal fill,
                              DateTime time, int barIndex, int qty, decimal entryCosts,
                              ConditionGroup? slRuleGroup, ConditionGroup? tpRuleGroup)
    {
        p.IsOpen = true;
        p.Direction = rule.Direction;
        p.EntryTime = time;
        p.EntryPrice = fill;
        p.EntryBarIndex = barIndex;
        p.Quantity = qty;
        p.EntryCosts = entryCosts;
        p.MAE = 0m;
        p.MFE = 0m;
        p.StopLossRuleGroup = null;
        p.TargetRuleGroup = null;
        p.StopLossPrice = null;
        p.TargetPrice = null;

        if (rule.StopLoss is { } sl)
        {
            if (sl.Mode == StopLossMode.Percentage && sl.Value > 0m)
                p.StopLossPrice = rule.Direction == Direction.Long
                    ? fill * (1m - sl.Value / 100m)
                    : fill * (1m + sl.Value / 100m);
            else if (sl.Mode == StopLossMode.Points && sl.Value > 0m)
                p.StopLossPrice = rule.Direction == Direction.Long
                    ? fill - sl.Value
                    : fill + sl.Value;
            else if (sl.Mode == StopLossMode.Rule)
                p.StopLossRuleGroup = slRuleGroup;
        }

        if (rule.Target is { } tp)
        {
            if (tp.Mode == TargetMode.Percentage && tp.Value > 0m)
                p.TargetPrice = rule.Direction == Direction.Long
                    ? fill * (1m + tp.Value / 100m)
                    : fill * (1m - tp.Value / 100m);
            else if (tp.Mode == TargetMode.Points && tp.Value > 0m)
                p.TargetPrice = rule.Direction == Direction.Long
                    ? fill + tp.Value
                    : fill - tp.Value;
            else if (tp.Mode == TargetMode.Rule)
                p.TargetRuleGroup = tpRuleGroup;
        }
    }

    private (bool triggered, decimal exitPrice, ExitReason reason) CheckIntrabarExit(
        PositionState p, decimal high, decimal low)
    {
        if (p.Direction == Direction.Long)
        {
            bool slHit = p.StopLossPrice.HasValue && low <= p.StopLossPrice.Value;
            bool tpHit = p.TargetPrice.HasValue && high >= p.TargetPrice.Value;
            if (slHit)
            {
                var fill = p.StopLossPrice!.Value - _slippageModel.Compute(p.StopLossPrice.Value);
                return (true, fill, ExitReason.StopLoss);
            }
            if (tpHit)
            {
                var fill = p.TargetPrice!.Value - _slippageModel.Compute(p.TargetPrice.Value);
                return (true, fill, ExitReason.Target);
            }
        }
        else
        {
            bool slHit = p.StopLossPrice.HasValue && high >= p.StopLossPrice.Value;
            bool tpHit = p.TargetPrice.HasValue && low <= p.TargetPrice.Value;
            if (slHit)
            {
                var fill = p.StopLossPrice!.Value + _slippageModel.Compute(p.StopLossPrice.Value);
                return (true, fill, ExitReason.StopLoss);
            }
            if (tpHit)
            {
                var fill = p.TargetPrice!.Value + _slippageModel.Compute(p.TargetPrice.Value);
                return (true, fill, ExitReason.Target);
            }
        }
        return (false, 0m, ExitReason.ExitCondition);
    }

    private Trade CloseTrade(int id, RuleDefinition rule, PositionState p,
                             decimal exitFill, DateTime exitTime, int exitBarIndex,
                             ExitReason reason)
    {
        decimal gross = p.Direction == Direction.Long
            ? (exitFill - p.EntryPrice) * p.Quantity
            : (p.EntryPrice - exitFill) * p.Quantity;

        var ctx = new TradeExecutionContext
        {
            EntryPrice = p.EntryPrice,
            ExitPrice = exitFill,
            Quantity = p.Quantity,
            Direction = p.Direction,
            Mode = rule.ExecutionMode
        };
        var costs = _costModel.Compute(ctx);
        decimal net = gross - costs.Total;

        int barsHeld = exitBarIndex - p.EntryBarIndex;
        if (barsHeld < 1) barsHeld = 1;
        decimal daysHeld = (decimal)(exitTime - p.EntryTime).TotalDays;

        return new Trade
        {
            TradeId = id,
            Direction = p.Direction,
            EntryTime = p.EntryTime,
            ExitTime = exitTime,
            EntryPrice = p.EntryPrice,
            ExitPrice = exitFill,
            Quantity = p.Quantity,
            GrossPnL = gross,
            Brokerage = costs.Brokerage,
            STT = costs.STT,
            ExchangeTxn = costs.ExchangeTxn,
            SEBI = costs.SEBI,
            StampDuty = costs.StampDuty,
            GST = costs.GST,
            TotalCosts = costs.Total,
            NetPnL = net,
            MAE = p.MAE,
            MFE = p.MFE,
            BarsHeld = barsHeld,
            DaysHeld = daysHeld,
            ExitReason = reason
        };
    }

    private static Dictionary<string, decimal> BuildParameters(RuleDefinition rule)
    {
        var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (rule.Parameters is null) return d;
        foreach (var p in rule.Parameters)
            if (!string.IsNullOrEmpty(p.Name)) d[p.Name] = p.Default;
        return d;
    }

    private BacktestResult EmptyResult(RuleDefinition rule, IBarSeries baseSeries)
    {
        var result = new BacktestResult
        {
            SchemaVersion = 1,
            Reproducibility = new ReproducibilityRecord
            {
                RuleJson = RuleDefinitionJson.Serialize(rule),
                DataFrom = baseSeries.From,
                DataTo = baseSeries.To,
                EngineVersion = EngineInfo.Version,
                RunAt = DateTime.UtcNow,
                DataSource = _options.DataSource,
                InstrumentToken = baseSeries.InstrumentToken,
                BaseTimeframe = baseSeries.Timeframe.ToString(),
                RunId = _options.RunId
            },
            Metrics = new BacktestMetrics(),
            Trades = Array.Empty<Trade>(),
            EquityCurve = Array.Empty<EquityPoint>()
        };
        result.RiskAssessment = RiskAssessmentCalculator.Compute(result, _options);
        return result;
    }
}
