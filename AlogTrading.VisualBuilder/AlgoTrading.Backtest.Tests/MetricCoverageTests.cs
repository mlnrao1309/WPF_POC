using System;
using System.Collections.Generic;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class MetricCoverageTests
{
    private static readonly DateTime D0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // CAGR
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Cagr_Zero_WhenNoTrades()
    {
        var eq = new EquityPoint[2];
        eq[0] = new EquityPoint(D0, 100000m);
        eq[1] = new EquityPoint(D0.AddYears(1), 100000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.CAGR);
    }

    [Fact]
    public void Cagr_Positive_WhenEquityGrows()
    {
        var eq = new EquityPoint[2];
        eq[0] = new EquityPoint(D0, 100000m);
        eq[1] = new EquityPoint(D0.AddYears(1), 120000m);

        var trade = new Trade
        {
            NetPnL = 20000m, GrossPnL = 20000m, TotalCosts = 0m,
            ExitReason = ExitReason.FullExit,
        };

        var m = BacktestMetrics.Compute(eq, new List<Trade> { trade },
            new BacktestConfig { InitialCapital = 100000m });

        // ~366 days / 365.25 â‰ˆ 1.002 years, 20% capital growth â†’ CAGR â‰ˆ 19.95%
        Assert.InRange(m.CAGR, 0.18m, 0.22m);
    }

    [Fact]
    public void Cagr_Zero_WhenSingleBar()
    {
        var eq = new EquityPoint[1];
        eq[0] = new EquityPoint(D0, 100000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.CAGR);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Max drawdown shape + duration
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void MaxDrawdown_MatchesHandCraftedShape()
    {
        decimal[] level = { 100000m, 105000m, 110000m, 108000m, 105000m,
                            100000m, 103000m, 108000m, 110000m, 112000m };
        var eq = new EquityPoint[level.Length];
        for (int i = 0; i < level.Length; i++)
            eq[i] = new EquityPoint(D0.AddDays(i), level[i]);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });

        Assert.Equal(10000m, m.MaxDrawdown);
        Assert.Equal(10000m / 110000m * 100m, m.MaxDrawdownPct, 4);
        Assert.Equal(6, m.MaxDrawdownDurationBars);
    }

    [Fact]
    public void MaxDrawdown_Zero_WhenMonotonicRise()
    {
        var eq = new EquityPoint[5];
        for (int i = 0; i < 5; i++) eq[i] = new EquityPoint(D0.AddDays(i), 100000m + i * 1000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.MaxDrawdown);
        Assert.Equal(0m, m.MaxDrawdownPct);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Sharpe
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Sharpe_Zero_WhenEquityFlat()
    {
        var eq = new EquityPoint[12];
        for (int i = 0; i < 12; i++)
            eq[i] = new EquityPoint(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i), 100000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.SharpeRatio);
    }

    [Fact]
    public void Sharpe_Positive_WhenEquityGrowsMonotonically()
    {
        var eq = new EquityPoint[12];
        for (int i = 0; i < 12; i++)
            eq[i] = new EquityPoint(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i), 100000m + i * 1000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.True(m.SharpeRatio > 0m);
    }

    [Fact]
    public void Sharpe_Negative_WhenEquityFallsMonotonically()
    {
        var eq = new EquityPoint[12];
        for (int i = 0; i < 12; i++)
            eq[i] = new EquityPoint(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i), 100000m - i * 1000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.True(m.SharpeRatio < 0m);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Sortino
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Sortino_Zero_WhenNoDownsideReturns()
    {
        // Strictly increasing equity â†’ no month with return below risk-free.
        var eq = new EquityPoint[12];
        for (int i = 0; i < 12; i++)
            eq[i] = new EquityPoint(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i), 100000m + i * 2000m);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.SortinoRatio);
    }

    [Fact]
    public void Sortino_Positive_WhenMostlyUpWithSomeDownMonths()
    {
        // Alternating up/down months, net upward trend.
        decimal[] level = {
            100000m, 105000m, 102000m, 108000m, 104000m, 111000m,
            107000m, 114000m, 110000m, 118000m, 113000m, 122000m,
        };
        var eq = new EquityPoint[level.Length];
        for (int i = 0; i < level.Length; i++)
            eq[i] = new EquityPoint(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(i), level[i]);

        var m = BacktestMetrics.Compute(eq, new List<Trade>(), new BacktestConfig { InitialCapital = 100000m });
        Assert.True(m.SortinoRatio > 0m);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Recovery Factor and Calmar
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void RecoveryFactor_IsZero_WhenNoDrawdown()
    {
        var eq = new EquityPoint[5];
        for (int i = 0; i < 5; i++) eq[i] = new EquityPoint(D0.AddDays(i), 100000m + i * 1000m);

        var trade = new Trade
        {
            NetPnL = 5000m, GrossPnL = 5000m, TotalCosts = 0m, ExitReason = ExitReason.FullExit,
        };

        var m = BacktestMetrics.Compute(eq, new List<Trade> { trade }, new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.RecoveryFactor);
    }

    [Fact]
    public void RecoveryFactor_EqualsNetOverMaxDrawdown()
    {
        decimal[] level = { 100000m, 105000m, 102000m, 100000m, 103000m, 106000m };
        var eq = new EquityPoint[level.Length];
        for (int i = 0; i < level.Length; i++)
            eq[i] = new EquityPoint(D0.AddDays(i), level[i]);

        var trade = new Trade
        {
            NetPnL = 6000m, GrossPnL = 6000m, TotalCosts = 0m, ExitReason = ExitReason.FullExit,
        };

        var m = BacktestMetrics.Compute(eq, new List<Trade> { trade }, new BacktestConfig { InitialCapital = 100000m });

        // MaxDD = 5000 (105000 â†’ 100000). RecoveryFactor = 6000 / 5000 = 1.2
        Assert.Equal(5000m, m.MaxDrawdown);
        Assert.Equal(1.2m, m.RecoveryFactor, 4);
    }

    [Fact]
    public void CalmarRatio_IsZero_WhenNoDrawdown()
    {
        var eq = new EquityPoint[5];
        for (int i = 0; i < 5; i++) eq[i] = new EquityPoint(D0.AddDays(i), 100000m + i * 1000m);

        var trade = new Trade
        {
            NetPnL = 5000m, GrossPnL = 5000m, TotalCosts = 0m, ExitReason = ExitReason.FullExit,
        };

        var m = BacktestMetrics.Compute(eq, new List<Trade> { trade }, new BacktestConfig { InitialCapital = 100000m });
        Assert.Equal(0m, m.CalmarRatio);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Average win / loss / profit factor / expectancy with pinned trades
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void TradeStats_ExactValuesWithHandCraftedTrades()
    {
        var trades = new List<Trade>
        {
            MakeTrade(2000m),   // win
            MakeTrade(-500m),   // loss
            MakeTrade(1500m),   // win
            MakeTrade(-1000m),  // loss
            MakeTrade(0m),      // breakeven
        };

        var eq = new EquityPoint[2];
        eq[0] = new EquityPoint(D0, 100000m);
        eq[1] = new EquityPoint(D0.AddDays(30), 102000m);

        var m = BacktestMetrics.Compute(eq, trades, new BacktestConfig { InitialCapital = 100000m });

        Assert.Equal(5, m.TotalTrades);
        Assert.Equal(2, m.WinningTrades);
        Assert.Equal(2, m.LosingTrades);
        Assert.Equal(1, m.BreakevenTrades);
        Assert.Equal(40m, m.WinRate);                 // 2/5 * 100
        Assert.Equal(1750m, m.AverageWin);            // (2000 + 1500) / 2
        Assert.Equal(-750m, m.AverageLoss);           // (-500 - 1000) / 2
        Assert.Equal(2000m, m.GrossPnL);              // 2000 - 500 + 1500 - 1000 + 0 (via NetPnL when GrossPnL = NetPnL)
        Assert.Equal(2.3333333333333333333333333333m, m.ProfitFactor, 4); // 3500 / 1500
        Assert.Equal(2000m, m.LargestWin);
        Assert.Equal(-1000m, m.LargestLoss);
    }

    private static Trade MakeTrade(decimal netPnL) => new Trade
    {
        NetPnL = netPnL, GrossPnL = netPnL, TotalCosts = 0m,
        ExitReason = ExitReason.FullExit,
    };

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // BarsHeld / DaysHeld population via real engine run
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void BarsHeldAndDaysHeld_ArePopulatedCorrectly()
    {
        // Same shape as EngineLoopTests: entry at bar 2, SL triggers at bar 4.
        decimal[] O = { 99m, 100m, 101m, 101m, 102m, 96m, 100m, 106m, 108m, 112m };
        decimal[] H = { 100m, 101m, 102m, 103m, 103m, 100m, 101m, 108m, 110m, 115m };
        decimal[] L = { 98m,  99m, 100m, 100m,  97m,  95m,  99m, 105m, 107m, 110m };
        decimal[] C = { 99.5m, 100.5m, 101.5m, 102.5m, 102m, 99m, 100.5m, 107m, 109m, 113m };

        var rule = Th.Rule(slValue: 2m, tpValue: 10m);
        var r = Th.Engine(rule, Th.Series(O, H, L, C)).Run();

        Assert.Single(r.Trades);
        var t = r.Trades[0];
        Assert.Equal(2, t.BarsHeld);       // 4 âˆ’ 2
        Assert.Equal(2m, t.DaysHeld, 4);   // 2 daily bars apart
    }
}
