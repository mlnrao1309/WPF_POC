using System;

namespace AlgoTrading.Backtest;

public sealed class BacktestResult
{
    public int SchemaVersion { get; init; } = 1;
    public ReproducibilityRecord Reproducibility { get; init; } = new ReproducibilityRecord();
    public BacktestMetrics Metrics { get; init; } = new BacktestMetrics();
    public Trade[] Trades { get; init; } = Array.Empty<Trade>();
    public EquityPoint[] EquityCurve { get; init; } = Array.Empty<EquityPoint>();
    public RejectedEntry[] RejectedEntries { get; init; } = Array.Empty<RejectedEntry>();
    public SweepResult? Sweep { get; init; }
    public WalkForwardResult? WalkForward { get; init; }
}