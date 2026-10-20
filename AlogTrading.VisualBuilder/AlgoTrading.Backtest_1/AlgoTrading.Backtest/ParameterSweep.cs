using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlgoTrading.Aggregation;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public sealed class SweepEntry
{
    public IReadOnlyDictionary<string, decimal> Parameters { get; init; } = new Dictionary<string, decimal>();
    public BacktestMetrics Metrics { get; init; } = new BacktestMetrics();
    public decimal CriterionValue { get; init; }
}

public sealed class SweepResult
{
    public SweepCriterion Criterion { get; init; }
    public int CombinationCount { get; init; }
    public IReadOnlyList<SweepEntry> Ranked { get; init; } = Array.Empty<SweepEntry>();
    public SweepEntry? Best { get; init; }
}

public static class ParameterSweep
{
    public static SweepResult Run(
        RuleDefinition rule, uint token, IBarSeries baseSeries, BacktestConfig cfg,
        ITransactionCostModel cost, ISlippageModel slip, IMarginModel margin,
        SweepCriterion? criterionOverride = null)
    {
        var crit = criterionOverride ?? cfg.DefaultSweepCriterion;

        var sweepable = new List<(string Name, decimal Min, decimal Max, decimal Step)>();
        if (rule.Parameters is not null)
        {
            for (int i = 0; i < rule.Parameters.Count; i++)
            {
                var p = rule.Parameters[i];
                if (p.Minimum != p.Maximum && p.Step > 0m)
                    sweepable.Add((p.Name, p.Minimum, p.Maximum, p.Step));
            }
        }

        var combos = new List<Dictionary<string, decimal>>();
        Build(sweepable, 0, new Dictionary<string, decimal>(StringComparer.Ordinal), combos);

        if (combos.Count > cfg.MaxSweepCombinations)
            throw new ArgumentException($"Sweep has {combos.Count} combinations, exceeding max {cfg.MaxSweepCombinations}.");

        if (combos.Count == 0)
        {
            var cache = new TimeframeCache(baseSeries);
            var eng = new BacktestEngine(rule, token, baseSeries, cache, cfg, cost, slip, margin);
            var res = eng.Run();
            var e = new SweepEntry { Parameters = new Dictionary<string, decimal>(), Metrics = res.Metrics, CriterionValue = Cv(res.Metrics, crit) };
            return new SweepResult { Criterion = crit, CombinationCount = 1, Ranked = new[] { e }, Best = e };
        }

        var bag = new ConcurrentBag<SweepEntry>();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.ForEach(combos, opts, combo =>
        {
            try
            {
                var clone = CloneWithParams(rule, combo);
                var cache = new TimeframeCache(baseSeries);
                var eng = new BacktestEngine(clone, token, baseSeries, cache, cfg, cost, slip, margin);
                var res = eng.Run();
                bag.Add(new SweepEntry
                {
                    Parameters = new Dictionary<string, decimal>(combo, StringComparer.Ordinal),
                    Metrics = res.Metrics,
                    CriterionValue = Cv(res.Metrics, crit),
                });
            }
            catch { /* skip failed combinations */ }
        });

        var ranked = bag.OrderByDescending(x => x.CriterionValue).ToArray();
        return new SweepResult
        {
            Criterion = crit,
            CombinationCount = combos.Count,
            Ranked = ranked,
            Best = ranked.Length > 0 ? ranked[0] : null,
        };
    }

    private static void Build(
        List<(string Name, decimal Min, decimal Max, decimal Step)> sw,
        int idx, Dictionary<string, decimal> cur, List<Dictionary<string, decimal>> outp)
    {
        if (idx >= sw.Count) { outp.Add(new Dictionary<string, decimal>(cur, StringComparer.Ordinal)); return; }
        var p = sw[idx];
        for (decimal v = p.Min; v <= p.Max; v += p.Step)
        {
            cur[p.Name] = v;
            Build(sw, idx + 1, cur, outp);
            if (outp.Count > 1000000) return;
        }
        cur.Remove(p.Name);
    }

    private static RuleDefinition CloneWithParams(RuleDefinition r, IReadOnlyDictionary<string, decimal> overrides)
    {
        string json = RuleDefinitionJson.Serialize(r);
        var clone = RuleDefinitionJson.Deserialize(json);
        if (clone.Parameters is not null)
            for (int i = 0; i < clone.Parameters.Count; i++)
                if (overrides.TryGetValue(clone.Parameters[i].Name, out var v))
                    clone.Parameters[i].Default = v;
        return clone;
    }

    private static decimal Cv(BacktestMetrics m, SweepCriterion c) => c switch
    {
        SweepCriterion.Sharpe       => m.SharpeRatio,
        SweepCriterion.ProfitFactor => m.ProfitFactor,
        SweepCriterion.NetPnL       => m.TotalNetPnL,
        SweepCriterion.Calmar       => m.CalmarRatio,
        _ => 0m,
    };
}