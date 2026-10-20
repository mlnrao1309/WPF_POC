using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class EngineLoopTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // -------- entry timing --------

    [Fact]
    public void Entry_Executes_At_Next_Bar_Open()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),   // bar 1: signal fires
            (102m, 103m, 101m, 102m),   // bar 2: entry executes here at O=102
            (103m, 104m, 102m, 103m),
            (104m, 105m, 103m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var rule = RuleBuilder.AlwaysEntryLong();

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(102m, result.Trades[0].EntryPrice);
        Assert.Equal(Start.AddDays(2), result.Trades[0].EntryTime);
        Assert.Equal(ExitReason.EndOfData, result.Trades[0].ExitReason);
        Assert.Equal(104m, result.Trades[0].ExitPrice);
        Assert.Equal(2m, result.Trades[0].GrossPnL);
        Assert.Equal(2m, result.Trades[0].NetPnL);
    }

    // -------- exit timing --------

    [Fact]
    public void FullExit_Signal_Executes_At_Next_Bar_Open()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 103m),   // bar 3: fullExit fires (close 103 > 102.5)
            (104m, 105m, 103m, 104m));  // bar 4: exit executes here at O=104

        var fullExit = RuleBuilder.AllOf(RuleBuilder.CloseGreaterThan(102.5m));
        var rule = RuleBuilder.AlwaysEntryLong(fullExit: fullExit);
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(102m, result.Trades[0].EntryPrice);
        Assert.Equal(104m, result.Trades[0].ExitPrice);
        Assert.Equal(ExitReason.FullExit, result.Trades[0].ExitReason);
        Assert.Equal(2m, result.Trades[0].GrossPnL);
    }

    // -------- intrabar SL / TP --------

    [Fact]
    public void Intrabar_StopLoss_Triggers_On_Low()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),   // bar 2: entry at O=102
            (102m, 103m, 99m, 102m),    // bar 3: low 99 touches SL
            (104m, 105m, 103m, 104m));

        // SL = 2% below entry (102 * 0.98 = 99.96); bar 3 low 99 <= 99.96
        var sl = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 2m };
        var rule = RuleBuilder.AlwaysEntryLong(sl: sl);
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(ExitReason.StopLoss, result.Trades[0].ExitReason);
        Assert.Equal(99.96m, result.Trades[0].ExitPrice);
        Assert.Equal(Start.AddDays(3), result.Trades[0].ExitTime);
    }

    [Fact]
    public void Intrabar_Target_Triggers_On_High()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),   // bar 2: entry at O=102
            (103m, 105m, 102m, 104m),   // bar 3: high 105 touches TP
            (104m, 105m, 103m, 104m));

        // TP = 2% above entry (102 * 1.02 = 104.04); bar 3 high 105 >= 104.04
        var tp = new TargetSpec { Mode = TargetMode.Percentage, Value = 2m };
        var rule = RuleBuilder.AlwaysEntryLong(tp: tp);
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(ExitReason.Target, result.Trades[0].ExitReason);
        Assert.Equal(104.04m, result.Trades[0].ExitPrice);
    }

    [Fact]
    public void SL_Wins_When_Both_Trigger_Same_Bar()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),   // bar 2: entry at 102
            (102m, 106m, 99m, 105m),    // bar 3: high 106 hits TP, low 99 hits SL
            (106m, 107m, 105m, 106m));

        var sl = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 2m };
        var tp = new TargetSpec { Mode = TargetMode.Percentage, Value = 2m };
        var rule = RuleBuilder.AlwaysEntryLong(sl: sl, tp: tp);
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(ExitReason.StopLoss, result.Trades[0].ExitReason);
    }

    [Fact]
    public void Points_Mode_SL()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (100m, 101m, 99m, 100m),
            (100m, 101m, 99m, 100m),   // bar 2: entry at 100
            (100m, 101m, 94m, 95m),    // bar 3: low 94 <= SL 95
            (95m, 96m, 94m, 95m));

        var sl = new StopLossSpec { Mode = StopLossMode.Points, Value = 5m };
        var rule = RuleBuilder.AlwaysEntryLong(sl: sl);
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());

        var result = engine.Run(rule, series);

        Assert.Single(result.Trades);
        Assert.Equal(ExitReason.StopLoss, result.Trades[0].ExitReason);
        Assert.Equal(95m, result.Trades[0].ExitPrice);
    }
    // -------- degenerate inputs --------

    [Fact]
    public void SingleBar_ProducesEmptyResult()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m));
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var rule = RuleBuilder.AlwaysEntryLong();

        var result = engine.Run(rule, series);

        Assert.Empty(result.Trades);
        Assert.Empty(result.EquityCurve);
    }

    [Fact]
    public void DisabledEntry_NeverTrades()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m));

        var rule = RuleBuilder.AlwaysEntryLong();
        rule.Entry.Enabled = false;

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(rule, series);

        Assert.Empty(result.Trades);
    }

    // -------- helper --------

    private static class EngineTestSeriesExt
    {
    }
}

// small shim used by Points_Mode_SL
internal static class EngineTestSeriesShim
{
}

file static class Ext
{
}