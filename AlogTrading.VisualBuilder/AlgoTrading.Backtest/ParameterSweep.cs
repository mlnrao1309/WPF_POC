using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

/// <summary>
/// Parameter grid sweep (Section 5.15, Delta 01.1). Only declared
/// ParameterSpec.Default values are substituted; indicator Params are not
/// swept. Per combination the rule is cloned via JSON round-trip and its
/// Parameters[i].Default is mutated before running the engine. Combinations
/// are evaluated in parallel with a bounded degree of parallelism. The
/// combination count is capped; exceeding the cap throws ArgumentException
/// with no partial results.
/// </summary>
public static class ParameterSweep
{
    public static SweepResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions options,
        SweepCriterion criterion,
        int maxCombinations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        if (maxCombinations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations),
                "maxCombinations must be >= 1.");

        var specs = rule.Parameters ?? new List<ParameterSpec>();

        var grids = BuildGrids(specs);
        long total = 1L;
        foreach (var g in grids)
        {
            total *= g.Count;
            if (total > maxCombinations)
                throw new ArgumentException(
                    "Sweep would generate " + total + " combinations, exceeding the cap of " +
                    maxCombinations + ".",
                    nameof(maxCombinations));
        }

        var combos = new List<decimal[]>((int)total);
        Enumerate(grids, 0, new decimal[grids.Count], combos);

        string frozen = RuleDefinitionJson.Serialize(rule);
        var engine = new BacktestEngine(options);
        var entries = new SweepEntry[combos.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
            CancellationToken = ct
        };

        Parallel.For(0, combos.Count, parallelOptions, idx =>
        {
            ct.ThrowIfCancellationRequested();

            var clone = RuleDefinitionJson.Deserialize(frozen);
            if (clone.Parameters is null)
                throw new InvalidOperationException(
                    "Sweep clone produced a rule with null Parameters.");

            for (int i = 0; i < specs.Count; i++)
            {
                if (i < clone.Parameters.Count)
                    clone.Parameters[i].Default = combos[idx][i];
            }

            var result = engine.Run(clone, series, ct);

            entries[idx] = new SweepEntry
            {
                Parameters = BuildParameterDict(specs, combos[idx]),
                Metrics = result.Metrics,
                CriterionValue = ExtractCriterion(result.Metrics, criterion)
            };
        });

        Array.Sort(entries, static (a, b) => b.CriterionValue.CompareTo(a.CriterionValue));

        return new SweepResult
        {
            Criterion = criterion,
            CombinationCount = entries.Length,
            Best = entries[0],
            Ranked = entries
        };
    }

    private static List<List<decimal>> BuildGrids(IReadOnlyList<ParameterSpec> specs)
    {
        var grids = new List<List<decimal>>(specs.Count);
        foreach (var s in specs)
        {
            var values = new List<decimal>();
            if (s.Minimum == s.Maximum)
            {
                values.Add(s.Minimum);
            }
            else
            {
                if (s.Step <= 0m)
                    throw new ArgumentException(
                        "Parameter '" + s.Name + "' has non-positive Step (" + s.Step + ").");
                for (decimal v = s.Minimum; v <= s.Maximum; v += s.Step)
                {
                    values.Add(v);
                    if (values.Count > 1_000_000)
                        throw new ArgumentException(
                            "Parameter '" + s.Name + "' grid exceeds 1,000,000 values; check Step.");
                }
            }
            grids.Add(values);
        }
        return grids;
    }

    private static void Enumerate(
        List<List<decimal>> grids, int depth, decimal[] current, List<decimal[]> output)
    {
        if (depth == grids.Count)
        {
            output.Add((decimal[])current.Clone());
            return;
        }
        foreach (var v in grids[depth])
        {
            current[depth] = v;
            Enumerate(grids, depth + 1, current, output);
        }
    }

    private static Dictionary<string, decimal> BuildParameterDict(
        IReadOnlyList<ParameterSpec> specs, decimal[] values)
    {
        var d = new Dictionary<string, decimal>(specs.Count, StringComparer.Ordinal);
        for (int i = 0; i < specs.Count; i++)
            d[specs[i].Name] = values[i];
        return d;
    }

    private static decimal ExtractCriterion(BacktestMetrics m, SweepCriterion c) => c switch
    {
        SweepCriterion.Sharpe       => m.Sharpe,
        SweepCriterion.ProfitFactor => m.ProfitFactor,
        SweepCriterion.NetPnL       => m.TotalNetPnL,
        SweepCriterion.Calmar       => m.Calmar,
        _                           => 0m
    };
}