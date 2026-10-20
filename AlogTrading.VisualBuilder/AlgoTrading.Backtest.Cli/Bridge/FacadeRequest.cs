namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>
/// Input to a backtest facade call. T7 does not depend on T3/T6 types
/// directly; the facade implementation is the sole contact point.
/// </summary>
public sealed class FacadeRequest
{
    public required string RuleJson { get; init; }
    public required string StrategyName { get; init; }
    public required uint InstrumentToken { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required RunMode Mode { get; init; }

    /// <summary>
    /// Upper bound on threads used by Parallel.ForEach inside a sweep
    /// (Section 5.15, Delta 01.1). Loaded from Backtest.MaxParallelism,
    /// clamped to [1, Environment.ProcessorCount - 1] at load time.
    /// </summary>
    public int MaxParallelism { get; init; } = 3;
}