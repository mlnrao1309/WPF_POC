using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class EquityCurveTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EquityCurve_Length_Equals_BarCount()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 103m),
            (104m, 105m, 103m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(series.Count, result.EquityCurve.Count);
    }

    [Fact]
    public void FirstEquityPoint_IsInitialCapital()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(100_000m, result.EquityCurve[0].Equity);
    }

    [Fact]
    public void Equity_Marks_OpenPosition_To_Market()
    {
        // bar 2: entry at 102, close at 103 â†’ unrealized +1
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 103m),
            (103m, 104m, 102m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions(quantity: 1));
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        // bar 2 close = 103, entry at 102 â†’ unrealized = +1
        Assert.Equal(100_001m, result.EquityCurve[2].Equity);
    }

    [Fact]
    public void FinalEquity_Equals_Initial_Plus_Realized()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 104m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions(quantity: 1));
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        // entry at 102, force close at last close 104 â†’ +2
        Assert.Equal(100_002m, result.EquityCurve[^1].Equity);
    }
}