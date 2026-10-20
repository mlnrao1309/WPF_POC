using AlgoTrading.Backtest.Contracts;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class SlippageTests
{
    [Fact]
    public void Spread_IsHalfSpread()
    {
        var model = new SpreadSlippageModel(0.05m);
        Assert.Equal(0.025m, model.Compute(100m));
    }

    [Fact]
    public void Spread_ScalesWithPrice()
    {
        var model = new SpreadSlippageModel(0.05m);
        Assert.Equal(0.25m, model.Compute(1000m));
    }

    [Fact]
    public void Spread_Zero_ReturnsZero()
    {
        var model = new SpreadSlippageModel(0m);
        Assert.Equal(0m, model.Compute(100m));
    }

    [Fact]
    public void Fixed_IsConstant()
    {
        var model = new FixedSlippageModel(0.50m);
        Assert.Equal(0.50m, model.Compute(100m));
        Assert.Equal(0.50m, model.Compute(9999m));
    }

    [Fact]
    public void ZeroSlippage_IsZero()
    {
        Assert.Equal(0m, ZeroSlippageModel.Instance.Compute(100m));
    }
}