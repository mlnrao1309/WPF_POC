namespace AlgoTrading.Backtest.Contracts;

public interface ISlippageModel
{
    /// <summary>Absolute slippage added to (long entry, short exit) or
    /// subtracted from (long exit, short entry) the reference price.</summary>
    decimal Compute(decimal referencePrice);
}