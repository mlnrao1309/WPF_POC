namespace AlgoTrading.Backtest;

public sealed class SweepResult
{
    public SweepCriterion Criterion { get; init; }
    public int TotalCombinations { get; init; }
    public int ValidCombinations { get; init; }
    public SweepEntry Best { get; init; } = new();
    public IReadOnlyList<SweepEntry> Ranked { get; init; } = Array.Empty<SweepEntry>();
}