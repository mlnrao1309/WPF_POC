using AlgoTrading.Backtest.Metrics;

namespace AlgoTrading.Backtest;

public sealed class WalkForwardWindow
{
    public int WindowIndex { get; init; }
    public DateTime ISWarmupFrom { get; init; }
    public DateTime ISFrom { get; init; }
    public DateTime ISTo { get; init; }
    public DateTime OOSWarmupFrom { get; init; }
    public DateTime OOSFrom { get; init; }
    public DateTime OOSTo { get; init; }

    public Dictionary<string, decimal> BestParameters { get; init; } = new();
    public BacktestMetrics ISMetrics { get; init; } = new();
    public BacktestMetrics OOSMetrics { get; init; } = new();
    public decimal DegradationRatio { get; init; }

    // M4: Record ALL rejected trials to prove isolation
    public List<string> RejectedTrials { get; init; } = new();

    // M4: Default to false, only true when OOS actually executed
    public bool TestWasFrozen { get; init; } = false;
}