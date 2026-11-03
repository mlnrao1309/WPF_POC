using AlgoTrading.Backtest.Metrics;

namespace AlgoTrading.Backtest;

public sealed class SweepEntry
{
    public Dictionary<string, decimal> Parameters { get; init; } = new();
    public BacktestMetrics Metrics { get; init; } = new();
    public decimal CriterionValue { get; init; }

    // M3: Audit trail for rejected candidates
    public bool IsValid { get; init; } = true;
    public List<string> ValidationErrors { get; init; } = new();
}