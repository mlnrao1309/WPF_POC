namespace AlgoTrading.Backtest;

/// <summary>
/// Walk-forward analysis configuration (Section 5.16).
/// </summary>
public sealed class WalkForwardConfig
{
    /// <summary>Fraction of each window used for in-sample optimization, in (0,1).</summary>
    public double InSampleRatio { get; init; } = 0.7;

    /// <summary>Number of disjoint windows that tile the full date range.</summary>
    public int WindowCount { get; init; } = 5;
}