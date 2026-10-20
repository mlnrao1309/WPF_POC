namespace AlgoTrading.Backtest;

/// <summary>
/// Result of a parameter sweep (Section 5.15). Ranked is sorted by
/// CriterionValue descending; Best is Ranked[0].
/// </summary>
public sealed class SweepResult
{
    public SweepCriterion Criterion { get; init; }
    public int CombinationCount { get; init; }
    public SweepEntry Best { get; init; } = new();
    public IReadOnlyList<SweepEntry> Ranked { get; init; } = Array.Empty<SweepEntry>();
}