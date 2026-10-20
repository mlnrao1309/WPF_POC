using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;

namespace AlgoTrading.Backtest.Results;

public sealed class BacktestResult
{
    public int SchemaVersion { get; init; } = 1;
    public ReproducibilityRecord Reproducibility { get; init; } = new();
    public BacktestMetrics Metrics { get; init; } = new();
    public IReadOnlyList<Trade> Trades { get; init; } = Array.Empty<Trade>();
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = Array.Empty<EquityPoint>();
    public SweepResult? Sweep { get; set; }
    public WalkForwardResult? WalkForward { get; set; }
}