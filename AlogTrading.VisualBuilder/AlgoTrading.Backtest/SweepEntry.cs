using AlgoTrading.Backtest.Metrics;

namespace AlgoTrading.Backtest;

/// <summary>
/// One parameter combination's result within a sweep (Section 5.15).
/// </summary>
public sealed class SweepEntry
{
    public Dictionary<string, decimal> Parameters { get; init; } = new();

    public BacktestMetrics Metrics { get; init; } = new();

    public decimal CriterionValue { get; init; }
}