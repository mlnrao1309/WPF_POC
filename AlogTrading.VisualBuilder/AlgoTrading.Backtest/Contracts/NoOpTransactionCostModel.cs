namespace AlgoTrading.Backtest.Contracts;

public sealed class NoOpTransactionCostModel : ITransactionCostModel
{
    public static readonly NoOpTransactionCostModel Instance = new();
    public CostBreakdown Compute(TradeExecutionContext ctx) => new();
}