using System;
using System.Collections.Generic;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public static class EngineVersion { public const string Value = "1.0.0"; }

public sealed class BacktestEngine
{
    private readonly RuleDefinition _rule;
    private readonly uint _token;
    private readonly IBarSeries _base;
    private readonly BacktestConfig _cfg;
    private readonly ITransactionCostModel _cost;
    private readonly ISlippageModel _slip;
    private readonly IMarginModel _margin;
    private readonly TimeframeCache _cache;

    public BacktestEngine(RuleDefinition rule, uint token, IBarSeries baseSeries, TimeframeCache cache,
        BacktestConfig cfg, ITransactionCostModel cost, ISlippageModel slip, IMarginModel margin)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _token = token;
        _base = baseSeries ?? throw new ArgumentNullException(nameof(baseSeries));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _cost = cost ?? throw new ArgumentNullException(nameof(cost));
        _slip = slip ?? throw new ArgumentNullException(nameof(slip));
        _margin = margin ?? throw new ArgumentNullException(nameof(margin));
    }

    public BacktestResult Run()
    {
        var refTfs = CollectTimeframes(_rule);
        refTfs.Add(_rule.EvaluationTimeframe);
        var aligner = new TimeframeAligner(_base, _cache, _token, refTfs);

        var idx = new NamedGroupIndex();
        idx.Index(_rule);

        var paramsDict = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (_rule.Parameters is not null)
            for (int i = 0; i < _rule.Parameters.Count; i++) paramsDict[_rule.Parameters[i].Name] = _rule.Parameters[i].Default;

        var compiler = new RuleCompiler(aligner, _rule.EvaluationTimeframe, paramsDict);

        var entryNode       = compiler.CompileGroup(_rule.Entry);
        var eligNode        = compiler.CompileGroup(_rule.Eligibility);
        var protNode        = compiler.CompileGroup(_rule.Protection);
        var exitNode        = compiler.CompileGroup(_rule.Exit);
        var fullExitNode    = compiler.CompileGroup(_rule.FullExit);

        CompiledNode? slRuleNode = null, tpRuleNode = null;
        if (_rule.StopLoss is not null && _rule.StopLoss.Mode == StopLossMode.Rule && !string.IsNullOrEmpty(_rule.StopLoss.GroupName))
            slRuleNode = compiler.CompileGroup(idx.Get(_rule.StopLoss.GroupName));
        if (_rule.Target is not null && _rule.Target.Mode == TargetMode.Rule && !string.IsNullOrEmpty(_rule.Target.GroupName))
            tpRuleNode = compiler.CompileGroup(idx.Get(_rule.Target.GroupName));

        int barCount = _base.Count;
        var bdt   = _base.DateTime;
        var bOpen = _base.Open;
        var bHigh = _base.High;
        var bLow  = _base.Low;
        var bClose = _base.Close;

        var eq = new EquityPoint[barCount];
        var trades = new List<Trade>();
        var rejected = new List<RejectedEntry>();

        decimal availCap = _cfg.InitialCapital;
        int nextId = 1;

        TradeRecord? open = null;
        bool pendingExit = false;
        bool pendingEntry = false;

        if (barCount > 0) eq[0] = new EquityPoint(bdt.Span[0], _cfg.InitialCapital);

        for (int i = 1; i < barCount; i++)
        {
            if (pendingExit && open is not null)
            {
                decimal fill = ExitFill(bOpen.Span[i], _rule.Direction);
                var tr = CloseTrade(open, fill, bdt.Span[i], i, ExitReason.ExitCondition);
                trades.Add(tr); availCap += tr.NetPnL;
                open = null; pendingExit = false;
            }

            if (pendingEntry && open is null)
            {
                decimal fill = EntryFill(bOpen.Span[i], _rule.Direction);
                decimal req = _margin.RequiredMargin(fill, _cfg.FixedQuantity, _rule.ExecutionMode);
                if (req > availCap)
                {
                    rejected.Add(new RejectedEntry
                    {
                        BarIndex = i, DateTime = bdt.Span[i], Reason = "Insufficient margin",
                        RequiredMargin = req, AvailableCapital = availCap,
                    });
                }
                else
                {
                    open = OpenTrade(i, fill, bdt.Span[i], nextId++, _cfg.FixedQuantity);
                }
                pendingEntry = false;
            }

            if (open is not null)
            {
                open.UpdateExcursions(_rule.Direction, bHigh.Span[i], bLow.Span[i]);
                if (TrySlTp(open, bHigh.Span[i], bLow.Span[i], out decimal px, out ExitReason why))
                {
                    decimal fill = ExitFill(px, _rule.Direction);
                    var tr = CloseTrade(open, fill, bdt.Span[i], i, why);
                    trades.Add(tr); availCap += tr.NetPnL;
                    open = null;
                }
            }

            eq[i] = new EquityPoint(bdt.Span[i], availCap + (open is not null ? Unreal(open, bClose.Span[i]) : 0m));

            bool e  = eligNode.Evaluate(i);
            bool en = entryNode.Evaluate(i);
            bool pr = protNode.Evaluate(i);
            bool ex = exitNode.Evaluate(i);
            bool fx = fullExitNode.Evaluate(i);

            if (open is not null)
            {
                if (slRuleNode is not null && slRuleNode.Evaluate(i)) pr = true;
                if (tpRuleNode is not null && tpRuleNode.Evaluate(i)) pr = true;
            }

            if (open is not null)
            {
                if (fx || pr || ex) pendingExit = true;
            }
            else if (e && en)
            {
                pendingEntry = true;
            }
        }

        if (open is not null && barCount > 0)
        {
            int last = barCount - 1;
            var tr = CloseTrade(open, bClose.Span[last], bdt.Span[last], last, ExitReason.EndOfData);
            trades.Add(tr); availCap += tr.NetPnL;
            eq[last] = new EquityPoint(bdt.Span[last], availCap);
            open = null;
        }

        var metrics = BacktestMetrics.Compute(eq, trades, _cfg);

        var repro = new ReproducibilityRecord
        {
            RuleJson = RuleDefinitionJson.Serialize(_rule),
            ParametersJson = JsonParams(paramsDict),
            DataFrom = barCount > 0 ? bdt.Span[0] : default,
            DataTo   = barCount > 0 ? bdt.Span[barCount - 1] : default,
            CostConfigJson = "",
            SlippageConfigJson = "",
            MarginConfigJson = "",
            EngineVersion = EngineVersion.Value,
            RunAt = DateTime.UtcNow,
            DataSource = "Sql",
            InstrumentToken = _token,
            BaseTimeframe = _base.Timeframe,
            RunId = null,
        };

        return new BacktestResult
        {
            SchemaVersion = 1,
            Reproducibility = repro,
            Metrics = metrics,
            Trades = trades.ToArray(),
            EquityCurve = eq,
            RejectedEntries = rejected.ToArray(),
        };
    }

    private TradeRecord OpenTrade(int barIdx, decimal fill, DateTime time, int id, int qty)
    {
        var r = new TradeRecord
        {
            TradeId = id, Direction = _rule.Direction, EntryBarIndex = barIdx,
            EntryTime = time, EntryPrice = fill, Quantity = qty,
        };
        if (_rule.StopLoss is not null && _rule.StopLoss.Mode != StopLossMode.Rule)
        {
            decimal v = _rule.StopLoss.Value ?? 0m;
            r.StopLossPrice = _rule.StopLoss.Mode == StopLossMode.Percentage
                ? (_rule.Direction == Direction.Long ? fill * (1m - v / 100m) : fill * (1m + v / 100m))
                : (_rule.Direction == Direction.Long ? fill - v : fill + v);
            r.HasStopLoss = true;
        }
        if (_rule.Target is not null && _rule.Target.Mode != TargetMode.Rule)
        {
            decimal v = _rule.Target.Value ?? 0m;
            r.TargetPrice = _rule.Target.Mode == TargetMode.Percentage
                ? (_rule.Direction == Direction.Long ? fill * (1m + v / 100m) : fill * (1m - v / 100m))
                : (_rule.Direction == Direction.Long ? fill + v : fill - v);
            r.HasTarget = true;
        }
        return r;
    }

    private static bool TrySlTp(TradeRecord p, decimal hi, decimal lo, out decimal px, out ExitReason why)
    {
        px = 0m; why = ExitReason.StopLoss;
        if (p.Direction == Direction.Long)
        {
            bool sl = p.HasStopLoss && lo <= p.StopLossPrice;
            bool tp = p.HasTarget   && hi >= p.TargetPrice;
            if (sl) { px = p.StopLossPrice; why = ExitReason.StopLoss; return true; }
            if (tp) { px = p.TargetPrice;  why = ExitReason.Target;   return true; }
        }
        else
        {
            bool sl = p.HasStopLoss && hi >= p.StopLossPrice;
            bool tp = p.HasTarget   && lo <= p.TargetPrice;
            if (sl) { px = p.StopLossPrice; why = ExitReason.StopLoss; return true; }
            if (tp) { px = p.TargetPrice;  why = ExitReason.Target;   return true; }
        }
        return false;
    }

    private Trade CloseTrade(TradeRecord p, decimal fill, DateTime time, int barIdx, ExitReason why)
    {
        int q = p.Quantity;
        decimal gross = p.Direction == Direction.Long ? (fill - p.EntryPrice) * q : (p.EntryPrice - fill) * q;
        var cb = _cost.Compute(p.Direction, _rule.ExecutionMode, p.EntryPrice, fill, q);
        return new Trade
        {
            TradeId = p.TradeId, Direction = p.Direction,
            EntryTime = p.EntryTime, ExitTime = time,
            EntryPrice = p.EntryPrice, ExitPrice = fill, Quantity = q,
            GrossPnL = gross,
            Brokerage = cb.Brokerage, STT = cb.STT, ExchangeTxn = cb.ExchangeTxn,
            SEBI = cb.SEBI, StampDuty = cb.StampDuty, GST = cb.GST, TotalCosts = cb.Total,
            NetPnL = gross - cb.Total,
            MAE = p.MAE, MFE = p.MFE,
            BarsHeld = barIdx - p.EntryBarIndex,
            DaysHeld = (decimal)(time - p.EntryTime).TotalDays,
            ExitReason = why,
        };
    }

    private static decimal Unreal(TradeRecord p, decimal m)
        => p.Direction == Direction.Long ? (m - p.EntryPrice) * p.Quantity : (p.EntryPrice - m) * p.Quantity;

    private decimal EntryFill(decimal px, Direction d)
    {
        decimal s = _slip.Compute(px);
        return d == Direction.Long ? px + s : px - s;
    }

    private decimal ExitFill(decimal px, Direction d)
    {
        decimal s = _slip.Compute(px);
        return d == Direction.Long ? px - s : px + s;
    }

    private static HashSet<Timeframe> CollectTimeframes(RuleDefinition r)
    {
        var s = new HashSet<Timeframe>();
        A(s, r.Eligibility); A(s, r.Entry); A(s, r.Protection); A(s, r.Exit); A(s, r.FullExit);
        return s;
    }

    private static void A(HashSet<Timeframe> s, ConditionGroup? g)
    {
        if (g?.Children is null) return;
        var ch = (IReadOnlyList<Node>)g.Children;
        for (int i = 0; i < ch.Count; i++)
        {
            if (ch[i] is ConditionGroup cg) A(s, cg);
            else if (ch[i] is Condition c) { E(s, c.Left); E(s, c.Right); }
        }
    }

    private static void E(HashSet<Timeframe> s, ValueExpression? e)
    {
        switch (e)
        {
            case FieldExpression f when f.Timeframe.HasValue: s.Add(f.Timeframe.Value); break;
            case IndicatorExpression i:
                if (i.Timeframe.HasValue) s.Add(i.Timeframe.Value);
                E(s, i.Source); break;
            case CalculationExpression c:
                E(s, c.Left); E(s, c.Right); break;
        }
    }

    private static string JsonParams(IReadOnlyDictionary<string, decimal> p)
    {
        var sb = new System.Text.StringBuilder("{");
        bool first = true;
        foreach (var kv in p)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":")
              .Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }
}