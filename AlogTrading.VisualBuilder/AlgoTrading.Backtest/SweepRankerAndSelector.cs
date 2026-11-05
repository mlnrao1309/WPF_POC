using System;
using System.Collections.Generic;
using System.Linq;
using AlgoTrading.Backtest.Results;

namespace AlgoTrading.Backtest;

public sealed record QualifiedRankingResult
{
    public required IReadOnlyList<SweepEntry> QualifiedRankedEntries { get; init; }
    public SweepEntry? BestQualified { get; init; }
    public required string SelectionStatus { get; init; }
}

public static class SweepRankerAndSelector
{
    /// <summary>
    /// Ranks only qualified, comparable trials using objective scores, comparison tolerance,
    /// lower risk, simpler changes, and stable lexical tie-breaks (M3-15 / Tech §8.4).
    /// </summary>
    public static QualifiedRankingResult RankAndSelect(
        IEnumerable<SweepEntry> allEntries,
        SweepCriterion criterion,
        decimal comparisonTolerance = 0.0001m)
    {
        ArgumentNullException.ThrowIfNull(allEntries);

        // Filter strictly to valid / qualified entries
        var qualified = allEntries.Where(e => e.IsValid).ToList();

        if (qualified.Count == 0)
        {
            return new QualifiedRankingResult
            {
                QualifiedRankedEntries = Array.Empty<SweepEntry>(),
                BestQualified = null,
                SelectionStatus = "NoQualifiedCandidate"
            };
        }

        // Sort with strict deterministic tie-breaking rules
        qualified.Sort((a, b) =>
        {
            // 1. Primary Objective Comparison within Tolerance
            decimal diff = b.CriterionValue - a.CriterionValue;
            if (Math.Abs(diff) > comparisonTolerance)
            {
                return diff > 0 ? 1 : -1;
            }

            // 2. Lower Risk (Minimizing Max Drawdown)
            int ddCmp = a.Metrics.MaxDrawdownPct.CompareTo(b.Metrics.MaxDrawdownPct);
            if (ddCmp != 0) return ddCmp;

            // 3. Simpler Parameter Change (fewer parameter keys or shorter parameter string length)
            int paramCountCmp = a.Parameters.Count.CompareTo(b.Parameters.Count);
            if (paramCountCmp != 0) return paramCountCmp;

            // 4. Stable Lexical Candidate / Parameter Identity
            string strA = BuildParamString(a.Parameters);
            string strB = BuildParamString(b.Parameters);
            return string.CompareOrdinal(strA, strB);
        });

        return new QualifiedRankingResult
        {
            QualifiedRankedEntries = qualified,
            BestQualified = qualified[0],
            SelectionStatus = "QualifiedWinnerSelected"
        };
    }

    private static string BuildParamString(Dictionary<string, decimal> p)
        => string.Join("|", p.OrderBy(x => x.Key, StringComparer.Ordinal)
                              .Select(x => $"{x.Key}={x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
}