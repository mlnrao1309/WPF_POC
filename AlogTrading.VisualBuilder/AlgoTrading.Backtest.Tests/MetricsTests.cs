using AlgoTrading.Backtest.Metrics;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class MetricsTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<Trade> ThreeTrades() => new()
    {
        new Trade
        {
            TradeId = 1, Direction = Direction.Long, Quantity = 1,
            GrossPnL = 10m, NetPnL = 8m, TotalCosts = 2m,
            BarsHeld = 2, DaysHeld = 2, MAE = 1m, MFE = 3m,
            ExitReason = ExitReason.Target
        },
        new Trade
        {
            TradeId = 2, Direction = Direction.Long, Quantity = 1,
            GrossPnL = 20m, NetPnL = 18m, TotalCosts = 2m,
            BarsHeld = 3, DaysHeld = 3, MAE = 1m, MFE = 5m,
            ExitReason = ExitReason.Target
        },
        new Trade
        {
            TradeId = 3, Direction = Direction.Long, Quantity = 1,
            GrossPnL = -5m, NetPnL = -7m, TotalCosts = 2m,
            BarsHeld = 1, DaysHeld = 1, MAE = 5m, MFE = 1m,
            ExitReason = ExitReason.StopLoss
        }
    };

    private static List<EquityPoint> ThreeTradeEquity() => new()
    {
        new EquityPoint { DateTime = Start, Equity = 100_000m },
        new EquityPoint { DateTime = Start.AddDays(1), Equity = 100_008m },
        new EquityPoint { DateTime = Start.AddDays(2), Equity = 100_026m },
        new EquityPoint { DateTime = Start.AddDays(3), Equity = 100_019m }
    };

    [Fact]
    public void Counts_Are_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);

        Assert.Equal(3, m.TotalTrades);
        Assert.Equal(2, m.WinningTrades);
        Assert.Equal(1, m.LosingTrades);
        Assert.Equal(0, m.BreakevenTrades);
    }

    [Fact]
    public void Aggregate_PnL_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);

        Assert.Equal(25m, m.GrossPnL);
        Assert.Equal(19m, m.TotalNetPnL);
        Assert.Equal(6m, m.TotalCosts);
    }

    [Fact]
    public void WinRate_And_ProfitFactor_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);

        Assert.Equal(66.67m, m.WinRatePct);
        // sumWins = 8 + 18 = 26; |sumLosses| = 7
        Assert.Equal(3.71m, m.ProfitFactor);
    }

    [Fact]
    public void Averages_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);

        Assert.Equal(13m, m.AverageWin);
        Assert.Equal(-7m, m.AverageLoss);
        Assert.Equal(18m, m.LargestWin);
        Assert.Equal(-7m, m.LargestLoss);
    }

    [Fact]
    public void Drawdown_Correct()
    {
        var equity = new List<EquityPoint>
        {
            new EquityPoint { DateTime = Start, Equity = 100_000m },
            new EquityPoint { DateTime = Start.AddDays(1), Equity = 105_000m },
            new EquityPoint { DateTime = Start.AddDays(2), Equity = 103_000m },
            new EquityPoint { DateTime = Start.AddDays(3), Equity = 102_000m }
        };

        var m = MetricsCalculator.Compute(new List<Trade>(), equity, 100_000m, 2.0m);

        Assert.Equal(3_000m, m.MaxDrawdownAbsolute);
        Assert.Equal(2.86m, m.MaxDrawdownPct);
    }

    [Fact]
    public void Empty_Trades_ProducesZeroCounts()
    {
        var m = MetricsCalculator.Compute(new List<Trade>(),
            new List<EquityPoint> { new EquityPoint { DateTime = Start, Equity = 100_000m } },
            100_000m, 2.0m);

        Assert.Equal(0, m.TotalTrades);
        Assert.Equal(0m, m.TotalNetPnL);
        Assert.Equal(0m, m.WinRatePct);
    }

    [Fact]
    public void Expectancy_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);
        // (2/3)*13 + (1/3)*(-7) = 26/3 - 7/3 = 19/3 â‰ˆ 6.33
        Assert.Equal(6.33m, m.Expectancy);
    }

    [Fact]
    public void AvgMAE_AvgMFE_Correct()
    {
        var m = MetricsCalculator.Compute(ThreeTrades(), ThreeTradeEquity(), 100_000m, 2.0m);
        // MAE: (1 + 1 + 5) / 3 = 2.333 â†’ 2.33
        Assert.Equal(2.33m, m.AverageMAE);
        // MFE: (3 + 5 + 1) / 3 = 3
        Assert.Equal(3m, m.AverageMFE);
    }
}