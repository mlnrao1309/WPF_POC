using AlgoTrading.Data;

namespace AlgoTrading.Backtest;

public sealed class BacktestEngineOptions
{
    public decimal InitialCapital { get; init; } = 100_000m;
    public decimal RiskFreeRatePercent { get; init; } = 2.0m;
    public int Quantity { get; init; } = 1;
    public int MaxParallelism { get; init; } = 3;
    public string CostsJson { get; init; } = "";
    public string SlippageJson { get; init; } = "";
    public string MarginsJson { get; init; } = "";
    public string DataSource { get; init; } = "Sql";
    public Guid? RunId { get; init; }
    public IBarSeries? DailySeries { get; init; }
}
