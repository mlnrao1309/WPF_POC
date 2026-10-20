using AlgoTrading.Backtest.Metrics;

namespace AlgoTrading.Backtest;

/// <summary>
/// One walk-forward window: IS range for optimization, OOS range for
/// out-of-sample validation (Section 5.16).
/// </summary>
public sealed class WalkForwardWindow
{
    public int WindowIndex { get; init; }
    public DateTime ISFrom { get; init; }
    public DateTime ISTo { get; init; }
    public DateTime OOSFrom { get; init; }
    public DateTime OOSTo { get; init; }
    public Dictionary<string, decimal> BestParameters { get; init; } = new();
    public BacktestMetrics ISMetrics { get; init; } = new();
    public BacktestMetrics OOSMetrics { get; init; } = new();

    /// <summary>
    /// OOS Sharpe divided by IS Sharpe when IS Sharpe &gt; 0; otherwise 0.
    /// </summary>
    public decimal DegradationRatio { get; init; }
}