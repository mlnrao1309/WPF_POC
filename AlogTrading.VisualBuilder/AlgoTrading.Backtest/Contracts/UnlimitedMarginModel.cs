using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Contracts;

public sealed class UnlimitedMarginModel : IMarginModel
{
    public static readonly UnlimitedMarginModel Instance = new();
    public decimal Compute(decimal entryPrice, int quantity, ExecutionMode mode) => 0m;
}