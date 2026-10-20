using AlgoTrading.Backtest.Configs;
using AlgoTrading.Backtest.Contracts;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class CostModelTests
{
    [Fact]
    public void Delivery_Long_HandComputed()
    {
        var model = new IndianEquityCostModel(CostsConfig.Defaults());
        var ctx = new TradeExecutionContext
        {
            EntryPrice = 100m, ExitPrice = 110m, Quantity = 10,
            Direction = Direction.Long, Mode = ExecutionMode.Delivery
        };

        var costs = model.Compute(ctx);

        Assert.Equal(0m, costs.Brokerage);
        Assert.Equal(2.10m, costs.STT);
        Assert.Equal(0.07m, costs.ExchangeTxn);
        Assert.Equal(0.00m, costs.SEBI);
        Assert.Equal(0.15m, costs.StampDuty);
        Assert.Equal(0.01m, costs.GST);
        Assert.Equal(2.33m, costs.Total);
    }

    [Fact]
    public void Intraday_Long_HandComputed()
    {
        var model = new IndianEquityCostModel(CostsConfig.Defaults());
        var ctx = new TradeExecutionContext
        {
            EntryPrice = 100m, ExitPrice = 110m, Quantity = 10,
            Direction = Direction.Long, Mode = ExecutionMode.Intraday
        };

        var costs = model.Compute(ctx);

        Assert.Equal(40m, costs.Brokerage);
        Assert.Equal(0.28m, costs.STT);
        Assert.Equal(0.07m, costs.ExchangeTxn);
        Assert.Equal(0.00m, costs.SEBI);
        Assert.Equal(0.03m, costs.StampDuty);
        Assert.Equal(7.21m, costs.GST);
        Assert.Equal(47.59m, costs.Total);
    }

    [Fact]
    public void Delivery_Short_SwapsTurnover()
    {
        var model = new IndianEquityCostModel(CostsConfig.Defaults());
        var ctx = new TradeExecutionContext
        {
            EntryPrice = 110m, ExitPrice = 100m, Quantity = 10,
            Direction = Direction.Short, Mode = ExecutionMode.Delivery
        };

        var costs = model.Compute(ctx);

        Assert.Equal(2.10m, costs.STT);
        Assert.Equal(0.07m, costs.ExchangeTxn);
        Assert.Equal(0.15m, costs.StampDuty);
    }

    [Fact]
    public void ZeroCosts_ProducesZero()
    {
        var model = new IndianEquityCostModel(CostsConfig.Parse(TestJson.ZeroCosts));
        var ctx = new TradeExecutionContext
        {
            EntryPrice = 100m, ExitPrice = 110m, Quantity = 10,
            Direction = Direction.Long, Mode = ExecutionMode.Delivery
        };
        var costs = model.Compute(ctx);
        Assert.Equal(0m, costs.Total);
    }

    [Fact]
    public void NoOp_Model_ReturnsZeroBreakdown()
    {
        var model = NoOpTransactionCostModel.Instance;
        var costs = model.Compute(new TradeExecutionContext
        {
            EntryPrice = 100m, ExitPrice = 110m, Quantity = 10,
            Direction = Direction.Long, Mode = ExecutionMode.Delivery
        });
        Assert.Equal(0m, costs.Total);
    }
}