using AlgoTrading.Backtest;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class WalkForwardTests
{
    private static RuleDefinition MakeRule()
    {
        return new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "WfTest",
            Direction = Direction.Long,
            EvaluationTimeframe = Timeframe.Parse("D"),
            ExecutionMode = ExecutionMode.Delivery,
            BaseTimeframe = Timeframe.Parse("D"),
            Parameters = new List<ParameterSpec>
            {
                new() { Name = "thr", Default = 50m, Minimum = 50m, Maximum = 50m, Step = 1m }
            },
            Entry = RuleBuilder.AllOf(new Condition
            {
                Enabled = true,
                Left = new FieldExpression { Field = Field.Close, Offset = 0 },
                Operator = Operator.GreaterThan,
                Right = new ParameterExpression { Name = "thr" }
            }),
            StopLoss = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 5m }
        };
    }

    private static IBarSeries Ramp(int n)
    {
        var bars = new (decimal, decimal, decimal, decimal)[n];
        for (int i = 0; i < n; i++)
        {
            decimal p = 100m + i;
            bars[i] = (p, p + 1m, p - 1m, p);
        }
        return EngineTestSeries.FromOhlc(
            Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc),
            bars);
    }

    [Fact]
    public void WindowCount_MatchesConfig()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 5 };
        var result = WalkForward.Run(
            MakeRule(), Ramp(100), RuleBuilder.ZeroCostOptions(), cfg, 1000);
        Assert.Equal(5, result.Windows.Count);
    }

    [Fact]
    public void WindowDates_CoverFullRange()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 4 };
        var series = Ramp(100);
        var result = WalkForward.Run(
            MakeRule(), series, RuleBuilder.ZeroCostOptions(), cfg, 1000);

        Assert.Equal(series.From, result.Windows[0].ISFrom);
        Assert.Equal(series.To, result.Windows[result.Windows.Count - 1].OOSTo);
    }

    [Fact]
    public void PerWindowMetrics_Present()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 3 };
        var result = WalkForward.Run(
            MakeRule(), Ramp(90), RuleBuilder.ZeroCostOptions(), cfg, 1000);

        foreach (var w in result.Windows)
        {
            Assert.NotNull(w.ISMetrics);
            Assert.NotNull(w.OOSMetrics);
            Assert.True(w.WindowIndex >= 0);
        }
    }

    [Fact]
    public void Aggregate_TotalTrades_MatchesSumOfOos()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 4 };
        var result = WalkForward.Run(
            MakeRule(), Ramp(120), RuleBuilder.ZeroCostOptions(), cfg, 1000);

        int expected = result.Windows.Sum(w => w.OOSMetrics.TotalTrades);
        Assert.Equal(expected, result.AggregateOOSMetrics.TotalTrades);
    }

    [Fact]
    public void ConsistencyScore_InRange()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 4 };
        var result = WalkForward.Run(
            MakeRule(), Ramp(120), RuleBuilder.ZeroCostOptions(), cfg, 1000);

        Assert.InRange(result.ConsistencyScore, 0.0, 1.0);
    }
}