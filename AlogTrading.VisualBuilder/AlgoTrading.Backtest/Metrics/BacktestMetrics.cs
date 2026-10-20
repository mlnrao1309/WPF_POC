namespace AlgoTrading.Backtest.Metrics;

public sealed class BacktestMetrics
{
    // Return metrics
    public decimal TotalNetPnL { get; init; }
    public decimal GrossPnL { get; init; }
    public decimal TotalCosts { get; init; }
    public decimal CostAsPctOfGross { get; init; }
    public decimal NetProfitPct { get; init; }
    public decimal Cagr { get; init; }

    // Trade statistics
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public int BreakevenTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal Expectancy { get; init; }
    public decimal AverageHoldingBars { get; init; }
    public decimal AverageHoldingDays { get; init; }
    public decimal TradesPerMonth { get; init; }

    // Risk metrics
    public decimal MaxDrawdownAbsolute { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public int DrawdownDurationBars { get; init; }
    public decimal Sharpe { get; init; }
    public decimal Sortino { get; init; }
    public decimal Calmar { get; init; }
    public decimal RecoveryFactor { get; init; }

    // Excursion
    public decimal AverageMAE { get; init; }
    public decimal AverageMFE { get; init; }
    public decimal MAEMFERatio { get; init; }
}