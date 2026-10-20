using AlgoTrading.Backtest.Metrics;

namespace AlgoTrading.Backtest;

/// <summary>
/// Walk-forward aggregate result (Section 5.16). Aggregate metrics are
/// computed from the concatenated OOS trade list and a rescaled OOS equity
/// curve (each window continues from the previous window's ending equity).
/// </summary>
public sealed class WalkForwardResult
{
    public IReadOnlyList<WalkForwardWindow> Windows { get; init; }
        = Array.Empty<WalkForwardWindow>();

    public BacktestMetrics AggregateOOSMetrics { get; init; } = new();
    public decimal AggregateOOSSharpe { get; init; }
    public decimal AggregateOOSProfitFactor { get; init; }
    public decimal AggregateOOSMaxDrawdownPct { get; init; }

    /// <summary>
    /// Fraction of windows with OOS Sharpe &gt; 0. In [0,1].
    /// </summary>
    public double ConsistencyScore { get; init; }
}