using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using System.Text;

namespace AlgoTrading.Backtest;

public static class ParameterSweep
{
    /// <summary>
    /// Legacy overload retained for internal callers using parameter specs.
    /// </summary>
    public static SweepResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions options,
        SweepCriterion criterion,
        int maxCombinations,
        CancellationToken ct = default)
    {
        var bindings = CreateBindingsFromSpecs(rule.Parameters ?? new List<ParameterSpec>());
        return Run(rule, series, options, criterion, bindings, validityCheck: null, maxCombinations, ct);
    }

    public static List<SweepBindingSpec> CreateBindingsFromSpecs(IReadOnlyList<ParameterSpec> specs)
    {
        var list = new List<SweepBindingSpec>();
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
                    throw new ArgumentException($"Parameter '{s.Name}' has non-positive Step ({s.Step}).");
                for (decimal v = s.Minimum; v <= s.Maximum; v += s.Step)
                {
                    values.Add(v);
                    if (values.Count > 1_000_000)
                        throw new ArgumentException($"Parameter '{s.Name}' grid exceeds 1,000,000 values; check Step.");
                }
            }
            list.Add(new SweepBindingSpec { Name = s.Name, JsonPointers = s.Name != null ? new[] { $"/parameters/{s.Name}" } : null, Values = values });
        }
        return list;
    }
    public static SweepResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions options,
        SweepCriterion criterion,
        IReadOnlyList<SweepBinding> bindings,
        Func<RuleDefinition, IEnumerable<string>>? validityCheck,
        int maxCombinations,
        CancellationToken ct = default)
    {
        var specs = bindings.Select(b => new SweepBindingSpec
        {
            Name = b.Name,
            JsonPointers = b.JsonPointer != null ? new[] { b.JsonPointer } : null,
            Values = b.Values
        }).ToList();

        return Run(rule, series, options, criterion, specs, validityCheck, maxCombinations, ct);
    }
    public static SweepResult Run(
        RuleDefinition rule,
        IBarSeries series,
        BacktestEngineOptions options,
        SweepCriterion criterion,
        IReadOnlyList<SweepBindingSpec> bindings,
        Func<RuleDefinition, IEnumerable<string>>? validityCheck,
        int maxCombinations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bindings);

        if (maxCombinations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations), "maxCombinations must be >= 1.");

        // 1. Baseline Validation
        if (validityCheck != null)
        {
            var baselineErrors = validityCheck(rule).ToList();
            if (baselineErrors.Count > 0)
            {
                return new SweepResult
                {
                    Criterion = criterion,
                    TotalCombinations = 0,
                    ValidCombinations = 0,
                    Best = new SweepEntry
                    {
                        IsValid = false,
                        ValidationErrors = baselineErrors.Prepend("Baseline rule failed validation.").ToList()
                    },
                    Ranked = Array.Empty<SweepEntry>()
                };
            }
        }

        string originalJson = RuleDefinitionJson.Serialize(rule);
        string originalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(originalJson))).ToLowerInvariant();
        var ruleObj = JsonNode.Parse(originalJson)!.AsObject();

        // 2. Preflight Validation & Pointer Binding Check (M3-03 & M3-06)
        SidecarBindingValidator.ValidateBindings(ruleObj, bindings);
        GridPreflightValidator.PreflightGrid(bindings, maxCombinations, series.Count, requiredMaxWarmupBars: 1);

        var grids = bindings.Select(b => b.Values.ToList()).ToList();
        long total = 1L;
        foreach (var g in grids) total *= g.Count;

        var combos = new List<decimal[]>((int)total);
        Enumerate(grids, 0, new decimal[grids.Count], combos);

        var engine = new BacktestEngine(options);
        var entries = new SweepEntry[combos.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
            CancellationToken = ct
        };

        // 3. Parallel Execution across Cartesian Tuples (M3-07 & M3-08)
        Parallel.For(0, combos.Count, parallelOptions, idx =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                string candidateId = $"cand_{idx:D4}";
                var candidateMeta = CandidateMaterializer.Materialize(
                    originalJson, originalHash, candidateId, bindings, combos[idx], validityCheck);

                if (!candidateMeta.IsValid)
                {
                    entries[idx] = new SweepEntry
                    {
                        Parameters = candidateMeta.BoundParameters,
                        IsValid = false,
                        ValidationErrors = candidateMeta.ValidationErrors.ToList()
                    };
                    return;
                }

                // Run trial through unified engine pipeline
                var result = engine.Run(candidateMeta.ClonedRule, series, ct);

                entries[idx] = new SweepEntry
                {
                    Parameters = candidateMeta.BoundParameters,
                    Metrics = result.Metrics,
                    CriterionValue = ExtractCriterion(result.Metrics, criterion),
                    IsValid = true
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                entries[idx] = new SweepEntry
                {
                    Parameters = BuildParameterDict(bindings, combos[idx]),
                    IsValid = false,
                    ValidationErrors = new List<string> { $"Trial failed: {ex.Message}" }
                };
            }
        });

        // 4. Deterministic Ranking & Tie-Breaking
        Array.Sort(entries, (a, b) =>
        {
            if (a.IsValid != b.IsValid) return a.IsValid ? -1 : 1;
            var cmp = b.CriterionValue.CompareTo(a.CriterionValue);
            if (cmp != 0) return cmp;
            cmp = a.Metrics.MaxDrawdownPct.CompareTo(b.Metrics.MaxDrawdownPct);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(BuildParamString(a.Parameters), BuildParamString(b.Parameters));
        });

        var validEntries = entries.Where(e => e.IsValid).ToArray();

        return new SweepResult
        {
            Criterion = criterion,
            TotalCombinations = entries.Length,
            ValidCombinations = validEntries.Length,
            Best = validEntries.Length > 0 ? validEntries[0] : new SweepEntry { IsValid = false },
            Ranked = entries
        };
    }

    private static string BuildParamString(Dictionary<string, decimal> p)
        => string.Join("|", p.OrderBy(x => x.Key, StringComparer.Ordinal)
                              .Select(x => $"{x.Key}={x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

    private static void Enumerate(List<List<decimal>> grids, int depth, decimal[] current, List<decimal[]> output)
    {
        if (depth == grids.Count) { output.Add((decimal[])current.Clone()); return; }
        foreach (var v in grids[depth]) { current[depth] = v; Enumerate(grids, depth + 1, current, output); }
    }

    private static Dictionary<string, decimal> BuildParameterDict(IReadOnlyList<SweepBindingSpec> bindings, decimal[] values)
    {
        var d = new Dictionary<string, decimal>(bindings.Count, StringComparer.Ordinal);
        for (int i = 0; i < bindings.Count; i++) d[bindings[i].Name] = values[i];
        return d;
    }

    private static decimal ExtractCriterion(BacktestMetrics m, SweepCriterion c) => c switch
    {
        SweepCriterion.Sharpe => m.Sharpe,
        SweepCriterion.ProfitFactor => m.ProfitFactor,
        SweepCriterion.NetPnL => m.TotalNetPnL,
        SweepCriterion.Calmar => m.Calmar,
        _ => 0m
    };
}