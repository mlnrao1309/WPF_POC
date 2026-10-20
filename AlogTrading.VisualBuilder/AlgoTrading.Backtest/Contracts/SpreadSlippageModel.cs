namespace AlgoTrading.Backtest.Contracts;

/// <summary>Section 5.11 spread-based model: spread = price * pct / 100; slippage = spread / 2.</summary>
public sealed class SpreadSlippageModel : ISlippageModel
{
    private readonly decimal _spreadPercent;

    public SpreadSlippageModel(decimal spreadPercent)
    {
        _spreadPercent = spreadPercent;
    }

    public decimal Compute(decimal referencePrice)
    {
        var spread = referencePrice * _spreadPercent / 100m;
        return spread / 2m;
    }
}