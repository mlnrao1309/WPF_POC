namespace AlgoTrading.Backtest.Contracts;

public sealed class ZeroSlippageModel : ISlippageModel
{
    public static readonly ZeroSlippageModel Instance = new();
    public decimal Compute(decimal referencePrice) => 0m;
}