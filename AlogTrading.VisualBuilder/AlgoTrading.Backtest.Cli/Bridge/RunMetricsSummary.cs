namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>Compact metrics summary used for console/log output only.</summary>
public sealed class RunMetricsSummary
{
    public int TotalTrades { get; init; }
    public decimal NetPnL { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal Sharpe { get; init; }
    public decimal MaxDrawdownPct { get; init; }
}