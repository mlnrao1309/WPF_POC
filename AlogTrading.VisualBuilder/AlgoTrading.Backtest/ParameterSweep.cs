using System.Text;
using System.Text.Json.Nodes;
using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public static class ParameterSweep
{
    /// <summary>
    /// Legacy overload retained only for internal callers that have not yet migrated to bindings.
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

    public static List<SweepBinding> CreateBindingsFromSpecs(IReadOnlyList<ParameterSpec> specs)
    {
        var list = new List<SweepBinding>();
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
            list.Add(new SweepBinding { Name = s.Name, JsonPointer = null, Values = values });
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
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bindings);

        if (maxCombinations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations), "maxCombinations must be >= 1.");

        // --- FIX 1: Validate baseline before enumerating any combination ---
        // If the baseline rule already fails the correctness gate, no combination can be trusted.
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

        var grids = bindings.Select(b => b.Values).ToList();
        long total = 1L;
        foreach (var g in grids)
        {
            total *= g.Count;
            if (total > maxCombinations)
                throw new ArgumentException($"Sweep would generate {total} combinations, exceeding the cap of {maxCombinations}.");
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

            try
            {
                var cloneObj = JsonNode.Parse(frozen)!.AsObject();

                for (int i = 0; i < bindings.Count; i++)
                {
                    var val = combos[idx][i];
                    var binding = bindings[i];

                    if (binding.JsonPointer != null)
                    {
                        ApplyJsonPointer(cloneObj, binding.JsonPointer, val);
                    }
                    else
                    {
                        if (cloneObj["parameters"] is JsonArray pArr)
                        {
                            bool found = false;
                            foreach (var item in pArr)
                            {
                                if (item?["name"]?.GetValue<string>() == binding.Name)
                                {
                                    item["default"] = val;
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                                throw new InvalidOperationException($"Declared parameter '{binding.Name}' not found in JSON.");
                        }
                        else
                        {
                            throw new InvalidOperationException($"JSON has no parameters array, cannot bind '{binding.Name}'.");
                        }
                    }
                }

                var clone = RuleDefinitionJson.Deserialize(cloneObj.ToJsonString());

                if (validityCheck != null)
                {
                    var errs = validityCheck(clone).ToList();
                    if (errs.Count > 0)
                    {
                        entries[idx] = new SweepEntry
                        {
                            Parameters = BuildParameterDict(bindings, combos[idx]),
                            IsValid = false,
                            ValidationErrors = errs
                        };
                        return;
                    }
                }

                var result = engine.Run(clone, series, ct);

                entries[idx] = new SweepEntry
                {
                    Parameters = BuildParameterDict(bindings, combos[idx]),
                    Metrics = result.Metrics,
                    CriterionValue = ExtractCriterion(result.Metrics, criterion),
                    IsValid = true
                };
            }
            // --- FIX 2: Re-throw cancellation, do not swallow it as a failed trial ---
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

        // --- FIX 3: Deterministic tie-break using sorted key=value pairs, not Dictionary.ToString() ---
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

    private static void ApplyJsonPointer(JsonNode root, string pointer, decimal value)
    {
        var tokens = DecodePointer(pointer);
        JsonNode? current = root;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(tokens[i], out var next) || next is null)
                    throw new InvalidOperationException($"JSON pointer '{pointer}' navigates missing property '{tokens[i]}'.");
                current = next;
            }
            else if (current is JsonArray arr && int.TryParse(tokens[i], out int idx))
            {
                if (idx < 0 || idx >= arr.Count)
                    throw new InvalidOperationException($"JSON pointer '{pointer}' array index '{tokens[i]}' out of range.");
                current = arr[idx];
            }
            else
            {
                throw new InvalidOperationException($"JSON pointer '{pointer}' cannot traverse {current?.GetType().Name ?? "null"} at '{tokens[i]}'.");
            }
        }

        var lastToken = tokens[^1];
        if (current is JsonObject parentObj)
        {
            // --- FIX 4: Replace requires existing property; no silent add ---
            if (!parentObj.ContainsKey(lastToken))
                throw new InvalidOperationException($"JSON pointer '{pointer}' targets missing property '{lastToken}'.");
            parentObj[lastToken] = JsonValue.Create(value);
        }
        else if (current is JsonArray parentArr && int.TryParse(lastToken, out int arrIdx))
        {
            if (arrIdx < 0 || arrIdx >= parentArr.Count)
                throw new InvalidOperationException($"JSON pointer '{pointer}' array index '{lastToken}' out of range.");
            parentArr[arrIdx] = JsonValue.Create(value);
        }
        else
        {
            throw new InvalidOperationException($"JSON pointer '{pointer}' cannot be assigned at '{lastToken}'.");
        }
    }

    // RFC 6901 unescape: ~1 -> /, ~0 -> ~
    private static string[] DecodePointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
            throw new InvalidOperationException($"JSON pointer '{pointer}' must start with '/'.");
        return pointer.TrimStart('/')
            .Split('/')
            .Select(seg => seg.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }

    private static void Enumerate(List<List<decimal>> grids, int depth, decimal[] current, List<decimal[]> output)
    {
        if (depth == grids.Count) { output.Add((decimal[])current.Clone()); return; }
        foreach (var v in grids[depth]) { current[depth] = v; Enumerate(grids, depth + 1, current, output); }
    }

    private static Dictionary<string, decimal> BuildParameterDict(IReadOnlyList<SweepBinding> bindings, decimal[] values)
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