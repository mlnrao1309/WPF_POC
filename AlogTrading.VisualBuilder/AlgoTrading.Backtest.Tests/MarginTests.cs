using AlgoTrading.Backtest.Configs;
using AlgoTrading.Backtest.Contracts;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class MarginTests
{
    [Fact]
    public void Delivery_IsFullNotional()
    {
        var model = new EquityMarginModel(MarginConfig.Defaults());
        Assert.Equal(1000m, model.Compute(100m, 10, ExecutionMode.Delivery));
    }

    [Fact]
    public void Intraday_IsLeveraged()
    {
        var model = new EquityMarginModel(MarginConfig.Defaults());
        Assert.Equal(200m, model.Compute(100m, 10, ExecutionMode.Intraday));
    }

    [Fact]
    public void Unlimited_ReturnsZero()
    {
        Assert.Equal(0m, UnlimitedMarginModel.Instance.Compute(100m, 10, ExecutionMode.Delivery));
    }
}