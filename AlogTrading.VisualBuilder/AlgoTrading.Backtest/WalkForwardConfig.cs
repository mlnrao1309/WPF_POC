namespace AlgoTrading.Backtest;

public enum BoundaryPolicy
{
    FlatAtBoundary,       // Close all open positions at the end of the slice
    CarryToNextWindow     // (Future) Carry positions across folds
}

public sealed class WalkForwardConfig
{
    public double InSampleRatio { get; init; } = 0.7;
    public int WindowCount { get; init; } = 5;

    // NEW: M4 Requirements
    public int WarmupDays { get; init; } = 30;
    public double FinalHoldoutRatio { get; init; } = 0.2;
    public BoundaryPolicy BoundaryPolicy { get; init; } = BoundaryPolicy.FlatAtBoundary;
}