using System.Text;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Configs;
using AlgoTrading.Backtest.Contracts;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Results;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

public sealed class BacktestEngine
{
    private readonly BacktestEngineOptions _options;
    private readonly CostsConfig _costs;
    private readonly SlippageConfig _slippage;
    private readonly MarginConfig _margins;
    private readonly ITransactionCostModel _costModel;
    private readonly ISlippageModel _slippageModel;
    private readonly IMarginModel _marginModel;

    private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

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

    private static DateTime ToIst(DateTime utcTime)
        => DateTime.SpecifyKind(utcTime.Add(IstOffset), DateTimeKind.Unspecified);

    public BacktestResult Run(RuleDefinition rule, IBarSeries baseSeries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(baseSeries);

        if (baseSeries.Count < 2)
            return EmptyResult(rule, baseSeries);

        var cache = new TimeframeCache(baseSeries, _options.DailySeries, _options.PreparedSeries);
        var accessor = new TimeframeAccessor(baseSeries.InstrumentToken, baseSeries, cache);
        var parameters = BuildParameters(rule);

        var references = new Dictionary<string, ReferenceValue>(StringComparer.OrdinalIgnoreCase);
        var indicators = new IndicatorProvider(accessor, parameters, references);
        var expr = new ExpressionEvaluator(accessor, indicators, parameters, references);
        var cond = new ConditionEvaluator(expr);

        var defaultTf = rule.EvaluationTimeframe;
        var slRuleGroup = rule.StopLoss?.Mode == StopLossMode.Rule
            ? NamedGroupFinder.Find(rule, rule.StopLoss.GroupName ?? "") : null;
        var tpRuleGroup = rule.Target?.Mode == TargetMode.Rule
            ? NamedGroupFinder.Find(rule, rule.Target.GroupName ?? "") : null;

        var trades = new List<Trade>();
        var equity = new List<EquityPoint>(baseSeries.Count);

        var traceLog = _options.EnableTracing ? new StringBuilder() : null;
        if (traceLog != null) traceLog.AppendLine("Time (IST),BarIndex,EventType,Identifier,Value,Details");

        var position = new PositionState();
        int tradeId = 0;
        decimal equityBase = _options.InitialCapital;

        bool pendingEntry = false;
        ExitReason? pendingExitReason = null;

        var closes = baseSeries.Close.Span;
        var highs = baseSeries.High.Span;
        var lows = baseSeries.Low.Span;
        var opens = baseSeries.Open.Span;
        var volumes = baseSeries.Volume.Span;
        var ois = baseSeries.OI.Span;
        var dts = baseSeries.DateTime.Span;

        int barCount = baseSeries.Count;

        equity.Add(new EquityPoint { DateTime = ToIst(dts[0]), Equity = equityBase });

        for (int i = 1; i < barCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (pendingExitReason.HasValue && position.IsOpen)
            {
                var fill = ApplyExitSlippage(position.Direction, opens[i]);
                var trade = CloseTrade(tradeId++, rule, position, fill, ToIst(dts[i]), i, pendingExitReason.Value);
                trades.Add(trade);
                equityBase += trade.NetPnL;
                position.Reset();
                pendingExitReason = null;
            }

            if (pendingEntry && !position.IsOpen)
            {
                var fill = ApplyEntrySlippage(rule.Direction, opens[i]);
                var qty = _options.Quantity;
                var requiredMargin = _marginModel.Compute(fill, qty, rule.ExecutionMode);
                var entryCosts = _costModel.Compute(new TradeExecutionContext
                {
                    EntryPrice = fill,
                    ExitPrice = 0m,
                    Quantity = qty,
                    Direction = rule.Direction,
                    Mode = rule.ExecutionMode,
                    Scope = TradeCostScope.Entry
                }).Total;

                if (requiredMargin + entryCosts <= equityBase)
                {
                    OpenPosition(position, rule, fill, ToIst(dts[i]), i, qty, entryCosts, slRuleGroup, tpRuleGroup, references);
                }
                pendingEntry = false;
            }

            bool intrabarExitHappened = false;
            if (position.IsOpen)
            {
                position.UpdateExcursions(highs[i], lows[i]);
                var (triggered, exitPrice, reason) = CheckIntrabarExit(position, highs[i], lows[i]);
                if (triggered)
                {
                    var trade = CloseTrade(tradeId++, rule, position, exitPrice, ToIst(dts[i]), i, reason);
                    trades.Add(trade);
                    equityBase += trade.NetPnL;
                    position.Reset();
                    intrabarExitHappened = true;
                }
            }

            if (intrabarExitHappened)
            {
                pendingExitReason = null;
                pendingEntry = false;
            }
            else
            {
                // [FIXED] Step 3.5: Evaluate Eligibility FIRST
                bool eligibility = rule.Eligibility is null || !rule.Eligibility.Enabled || cond.EvaluateGroup(rule.Eligibility, defaultTf, i);

                // [FIXED] Step 3.6: Evaluate References ONLY if eligible
                // Inside the BacktestEngine.cs loop, replace the entire Reference evaluation block with this:

                if (eligibility && rule.References != null)
                {
                    foreach (var refBlock in rule.References)
                    {
                        if (cond.Evaluate(refBlock.Trigger, refBlock.Timeframe, i))
                        {
                            var refTf = refBlock.Timeframe;
                            var alignedRef = accessor.Resolve(refTf);
                            var refIndex = alignedRef.BaseToTfIndex[i];

                            if (refIndex >= 0)
                            {
                                var timeIst = ToIst(dts[i]);
                                var refOpen = alignedRef.GetFieldSpan("Open")[refIndex];
                                var refHigh = alignedRef.GetFieldSpan("High")[refIndex];
                                var refLow = alignedRef.GetFieldSpan("Low")[refIndex];
                                var refClose = alignedRef.GetFieldSpan("Close")[refIndex];
                                var refVol = alignedRef.GetFieldSpan("Volume")[refIndex];
                                var refOi = alignedRef.GetFieldSpan("OI")[refIndex];
                                var candle = new Candle(timeIst, refOpen, refHigh, refLow, refClose, refVol, refOi);

                                foreach (var capture in refBlock.Captures)
                                {
                                    // Check if THIS specific capture already exists
                                    bool exists = references.ContainsKey(capture.Id);

                                    // If it exists, and we are NOT overwriting, skip this specific capture
                                    if (exists && !refBlock.OverwriteOnTrigger) continue;

                                    // Otherwise, evaluate and store
                                    if (capture.Value != null && expr.TryEvaluate(capture.Value, refBlock.Timeframe, i, 0, out var v))
                                    {
                                        references[capture.Id] = new ReferenceValue(candle, v, i);
                                        traceLog?.AppendLine($"{timeIst:yyyy-MM-dd HH:mm},{i},Variable,{capture.Id},{v},\"High={refHigh}, Low={refLow}, Close={refClose}\"");
                                    }
                                }
                            }
                        }
                    }
                }
                bool entry = rule.Entry is not null && rule.Entry.Enabled && cond.EvaluateGroup(rule.Entry, defaultTf, i);
                bool protection = rule.Protection is not null && rule.Protection.Enabled && cond.EvaluateGroup(rule.Protection, defaultTf, i);
                bool exit = rule.Exit is not null && rule.Exit.Enabled && cond.EvaluateGroup(rule.Exit, defaultTf, i);
                bool fullExit = rule.FullExit is not null && rule.FullExit.Enabled && cond.EvaluateGroup(rule.FullExit, defaultTf, i);

                bool slRule = slRuleGroup is not null && cond.EvaluateGroup(slRuleGroup, defaultTf, i);
                bool tpRule = tpRuleGroup is not null && cond.EvaluateGroup(tpRuleGroup, defaultTf, i);

                var timeIst2 = ToIst(dts[i]);
                if (entry) traceLog?.AppendLine($"{timeIst2:yyyy-MM-dd HH:mm},{i},Signal,Entry,True,\"\"");
                if (exit) traceLog?.AppendLine($"{timeIst2:yyyy-MM-dd HH:mm},{i},Signal,Exit,True,\"\"");
                if (fullExit) traceLog?.AppendLine($"{timeIst2:yyyy-MM-dd HH:mm},{i},Signal,FullExit,True,\"\"");

                _options.SignalObserver?.Invoke(new SignalObservation(
                    timeIst2, timeIst2,
                    eligibility, entry, protection, exit, fullExit, slRule, tpRule));

                if (position.IsOpen && i + 1 < barCount && (fullExit || protection || exit || slRule || tpRule))
                {
                    ExitReason reason;
                    if (fullExit) reason = ExitReason.FullExit;
                    else if (protection) reason = ExitReason.Protection;
                    else if (slRule) reason = ExitReason.StopLoss;
                    else if (tpRule) reason = ExitReason.Target;
                    else reason = ExitReason.ExitCondition;
                    pendingExitReason = reason;
                }

                if (!position.IsOpen && !pendingExitReason.HasValue && eligibility && entry && i + 1 < barCount)
                {
                    pendingEntry = true;
                }
            }

            decimal eq = equityBase;
            if (position.IsOpen)
            {
                decimal unrealized = position.Direction == Direction.Long
                    ? (closes[i] - position.EntryPrice) * position.Quantity
                    : (position.EntryPrice - closes[i]) * position.Quantity;
                eq += unrealized - position.EntryCosts;
            }
            equity.Add(new EquityPoint { DateTime = ToIst(dts[i]), Equity = eq });
        }

        if (position.IsOpen)
        {
            int last = barCount - 1;
            var fill = ApplyExitSlippage(position.Direction, closes[last]);
            var trade = CloseTrade(tradeId++, rule, position, fill, ToIst(dts[last]), last, ExitReason.EndOfData);
            trades.Add(trade);
            equityBase += trade.NetPnL;
            position.Reset();
            if (equity.Count > 0) equity[^1] = new EquityPoint { DateTime = ToIst(dts[last]), Equity = equityBase };
        }

        if (traceLog != null && traceLog.Length > 0)
        {
            var tracePath = Path.Combine(Environment.CurrentDirectory, $"trace_{rule.StrategyName}_{baseSeries.InstrumentToken}.csv");
            File.WriteAllText(tracePath, traceLog.ToString(), Encoding.UTF8);
            Console.WriteLine($"Trace log written to {tracePath}");
        }

        var metrics = MetricsCalculator.Compute(trades, equity, _options.InitialCapital, _options.RiskFreeRatePercent);

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
            EquityCurve = equity
        };
        result.RiskAssessment = RiskAssessmentCalculator.Compute(result, _options);
        return result;
    }

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

    private void OpenPosition(PositionState p, RuleDefinition rule, decimal fill, DateTime timeIst, int barIndex, int qty, decimal entryCosts, ConditionGroup? slRuleGroup, ConditionGroup? tpRuleGroup, Dictionary<string, ReferenceValue> currentReferences)
    {
        p.IsOpen = true;
        p.Direction = rule.Direction;
        p.EntryTime = timeIst;
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

        p.EntryReferences = new Dictionary<string, ReferenceValue>(currentReferences);

        if (rule.StopLoss is { } sl)
        {
            if (sl.Mode == StopLossMode.Percentage && sl.Value > 0m)
                p.StopLossPrice = rule.Direction == Direction.Long ? fill * (1m - sl.Value / 100m) : fill * (1m + sl.Value / 100m);
            else if (sl.Mode == StopLossMode.Points && sl.Value > 0m)
                p.StopLossPrice = rule.Direction == Direction.Long ? fill - sl.Value : fill + sl.Value;
            else if (sl.Mode == StopLossMode.Rule)
                p.StopLossRuleGroup = slRuleGroup;
        }

        if (rule.Target is { } tp)
        {
            if (tp.Mode == TargetMode.Percentage && tp.Value > 0m)
                p.TargetPrice = rule.Direction == Direction.Long ? fill * (1m + tp.Value / 100m) : fill * (1m - tp.Value / 100m);
            else if (tp.Mode == TargetMode.Points && tp.Value > 0m)
                p.TargetPrice = rule.Direction == Direction.Long ? fill + tp.Value : fill - tp.Value;
            else if (tp.Mode == TargetMode.Rule)
                p.TargetRuleGroup = tpRuleGroup;
        }
    }

    private (bool triggered, decimal exitPrice, ExitReason reason) CheckIntrabarExit(PositionState p, decimal high, decimal low)
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

    private Trade CloseTrade(int id, RuleDefinition rule, PositionState p, decimal exitFill, DateTime exitTimeIst, int exitBarIndex, ExitReason reason)
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
        decimal daysHeld = (decimal)(exitTimeIst - p.EntryTime).TotalDays;

        return new Trade
        {
            TradeId = id,
            Direction = p.Direction,
            EntryTime = p.EntryTime,
            ExitTime = exitTimeIst,
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
            ExitReason = reason,
            CapturedReferences = p.EntryReferences
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

