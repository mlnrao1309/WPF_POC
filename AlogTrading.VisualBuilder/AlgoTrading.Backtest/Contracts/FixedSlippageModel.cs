namespace AlgoTrading.Backtest.Contracts;

public sealed class FixedSlippageModel : ISlippageModel
{
    private readonly decimal _amount;

    public FixedSlippageModel(decimal amount) { _amount = amount; }

    public decimal Compute(decimal referencePrice) => _amount;
}