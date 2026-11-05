using AlgoTrading.Backtest;

public sealed record NeighborSensitivityReport
{
    public required string ParameterName { get; init; }
    public decimal TargetValue { get; init; }
    public decimal ObjectiveScore { get; init; }
    public decimal NeighborMinusScore { get; init; }
    public decimal NeighborPlusScore { get; init; }
    public bool IsIsolatedPeak { get; init; }
    public bool IsStablePlateau { get; init; }
}

public static class SweepSensitivityAnalyzer
{
    /// <summary>
    /// Analyzes neighbor sensitivity along single parameter axes for qualified sweep entries (M3-16).
    /// </summary>
    public static IReadOnlyList<NeighborSensitivityReport> AnalyzeSensitivity(
        IReadOnlyList<SweepEntry> rankedEntries,
        decimal sensitivityTolerance = 0.05m)
    {
        ArgumentNullException.ThrowIfNull(rankedEntries);
        var reports = new List<NeighborSensitivityReport>();

        // Map parameter keys to their observed values and objective scores
        var validEntries = rankedEntries.Where(e => e.IsValid).ToList();
        if (validEntries.Count < 3) return reports;

        foreach (var entry in validEntries)
        {
            foreach (var kvp in entry.Parameters)
            {
                string paramName = kvp.Key;
                decimal val = kvp.Value;

                // Find adjacent grid neighbors along this parameter axis
                var lowerNeighbor = validEntries
                    .Where(e => e.Parameters.ContainsKey(paramName) && e.Parameters[paramName] < val)
                    .OrderByDescending(e => e.Parameters[paramName])
                    .FirstOrDefault();

                var upperNeighbor = validEntries
                    .Where(e => e.Parameters.ContainsKey(paramName) && e.Parameters[paramName] > val)
                    .OrderBy(e => e.Parameters[paramName])
                    .FirstOrDefault();

                decimal currentScore = entry.CriterionValue;
                decimal minusScore = lowerNeighbor?.CriterionValue ?? currentScore;
                decimal plusScore = upperNeighbor?.CriterionValue ?? currentScore;

                bool isIsolated = (currentScore - minusScore > sensitivityTolerance) && (currentScore - plusScore > sensitivityTolerance);
                bool isPlateau = Math.Abs(currentScore - minusScore) <= sensitivityTolerance && Math.Abs(currentScore - plusScore) <= sensitivityTolerance;

                reports.Add(new NeighborSensitivityReport
                {
                    ParameterName = paramName,
                    TargetValue = val,
                    ObjectiveScore = currentScore,
                    NeighborMinusScore = minusScore,
                    NeighborPlusScore = plusScore,
                    IsIsolatedPeak = isIsolated,
                    IsStablePlateau = isPlateau
                });
            }
        }

        return reports;
    }

    /// <summary>
    /// Generates a complete human-readable summary report for the sweep execution (M3-16).
    /// </summary>
    public static string GenerateSummaryReport(
        int totalCombinations,
        int validCombinations,
        SweepCriterion criterion,
        SweepEntry? bestEntry,
        IReadOnlyList<NeighborSensitivityReport> sensitivityReports)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== M3 Parameter Sweep Summary Report ===");
        sb.AppendLine($"Criterion:         {criterion}");
        sb.AppendLine($"Total Combos:      {totalCombinations}");
        sb.AppendLine($"Valid/Qualified:   {validCombinations}");
        sb.AppendLine();

        if (bestEntry != null && bestEntry.IsValid)
        {
            sb.AppendLine("Best Qualified Candidate:");
            sb.AppendLine($"  Criterion Value: {bestEntry.CriterionValue:0.####}");
            sb.AppendLine($"  Sharpe Ratio:    {bestEntry.Metrics.Sharpe:0.####}");
            sb.AppendLine($"  Net P&L:         {bestEntry.Metrics.TotalNetPnL:N2}");
            sb.AppendLine($"  Max Drawdown:    {bestEntry.Metrics.MaxDrawdownPct:0.##}%");
            sb.AppendLine("  Parameters:");
            foreach (var p in bestEntry.Parameters)
            {
                sb.AppendLine($"    {p.Key} = {p.Value}");
            }
        }
        else
        {
            sb.AppendLine("Best Qualified Candidate: None (Inconclusive / No Qualified Winner)");
        }

        sb.AppendLine();
        sb.AppendLine("Neighbor Sensitivity Analysis:");
        if (sensitivityReports.Count == 0)
        {
            sb.AppendLine("  No neighbor sensitivity data available (insufficient entries).");
        }
        else
        {
            foreach (var rep in sensitivityReports.Take(10))
            {
                string status = rep.IsIsolatedPeak ? "[Isolated Peak]" : rep.IsStablePlateau ? "[Stable Plateau]" : "[Transition]";
                sb.AppendLine($"  Param '{rep.ParameterName}' = {rep.TargetValue}: Score={rep.ObjectiveScore:0.####} {status}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Note: Rankings reflect tested in-sample tuples and do not guarantee out-of-sample robustness.");
        return sb.ToString();
    }
}