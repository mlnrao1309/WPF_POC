using System;

namespace AlgoTrading.Backtest;

public interface ISlippageModel { decimal Compute(decimal referencePrice); }

public sealed class FixedSlippageModel : ISlippageModel
{
    private readonly decimal _a;
    public FixedSlippageModel(decimal a) { _a = a < 0m ? 0m : a; }
    public decimal Compute(decimal r) => _a;
}

public sealed class SpreadSlippageModel : ISlippageModel
{
    private readonly decimal _h;
    public SpreadSlippageModel(decimal spreadPct) { _h = (spreadPct < 0m ? 0m : spreadPct) / 200m; }
    public decimal Compute(decimal r) => Math.Abs(r) * _h;
}

public static class SlippageFactory
{
    public static ISlippageModel Create(SlippageConfig cfg)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        return cfg.Mode switch
        {
            SlippageMode.Fixed  => new FixedSlippageModel(cfg.FixedAmount),
            SlippageMode.Spread => new SpreadSlippageModel(cfg.SpreadPercent),
            _ => throw new ArgumentOutOfRangeException(nameof(cfg.Mode)),
        };
    }
}