using AlgoTrading.Backtest.Results;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class ReproducibilityTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EngineVersion_IsRecorded()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(EngineInfo.Version, result.Reproducibility.EngineVersion);
        Assert.Equal("1.0.0", EngineInfo.Version);
    }

    [Fact]
    public void SchemaVersion_Is_One()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m));
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesKeyFields()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m),
            (102m, 103m, 101m, 102m),
            (103m, 104m, 102m, 103m));

        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        var json = BacktestResultJson.Serialize(result);
        var restored = BacktestResultJson.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(result.SchemaVersion, restored!.SchemaVersion);
        Assert.Equal(result.Reproducibility.EngineVersion, restored.Reproducibility.EngineVersion);
        Assert.Equal(result.Reproducibility.InstrumentToken, restored.Reproducibility.InstrumentToken);
        Assert.Equal(result.Metrics.TotalTrades, restored.Metrics.TotalTrades);
        Assert.Equal(result.Trades.Count, restored.Trades.Count);
        Assert.Equal(result.EquityCurve.Count, restored.EquityCurve.Count);
    }

    [Fact]
    public void Result_Contains_No_Null_Reproducibility()
    {
        var series = EngineTestSeries.FromOhlc(Timeframe.Parse("D"), Start,
            (100m, 101m, 99m, 100m),
            (101m, 102m, 100m, 101m));
        var engine = new BacktestEngine(RuleBuilder.ZeroCostOptions());
        var result = engine.Run(RuleBuilder.AlwaysEntryLong(), series);

        Assert.NotNull(result.Reproducibility);
        Assert.NotNull(result.Metrics);
        Assert.NotNull(result.Trades);
        Assert.NotNull(result.EquityCurve);
    }
}