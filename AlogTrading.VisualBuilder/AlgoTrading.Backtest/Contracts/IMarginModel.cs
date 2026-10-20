using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Contracts;

public interface IMarginModel
{
    decimal Compute(decimal entryPrice, int quantity, ExecutionMode mode);
}