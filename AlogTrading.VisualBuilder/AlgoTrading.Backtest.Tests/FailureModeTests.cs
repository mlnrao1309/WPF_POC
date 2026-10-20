using AlgoTrading.Backtest;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class FailureModeTests
{
    private static RuleDefinition MakeRule(decimal min, decimal max, decimal step, decimal def)
    {
        return new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "FailTest",
            Direction = Direction.Long,
            EvaluationTimeframe = Timeframe.Parse("D"),
            ExecutionMode = ExecutionMode.Delivery,
            BaseTimeframe = Timeframe.Parse("D"),
            Parameters = new List<ParameterSpec>
            {
                new() { Name = "thr", Default = def, Minimum = min, Maximum = max, Step = step }
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
    public void SweepCap_Exceeded_ThrowsArgumentException()
    {
        var rule = MakeRule(0m, 100m, 1m, 50m);
        Assert.Throws<ArgumentException>(() => ParameterSweep.Run(
            rule, Ramp(10), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 100));
    }

    [Fact]
    public void EmptySeries_Sweep_DoesNotThrow()
    {
        var rule = MakeRule(50m, 50m, 1m, 50m);
        var emptySeries = EngineTestSeries.FromOhlc(
            Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc));

        var result = ParameterSweep.Run(
            rule, emptySeries, RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);

        Assert.Equal(1, result.CombinationCount);
        Assert.Equal(0, result.Best.Metrics.TotalTrades);
    }

    [Fact]
    public void SliceBarSeries_EmptyRange_CountZero()
    {
        var series = Ramp(10);
        var slice = new SliceBarSeries(
            series,
            new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2051, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, slice.Count);
        Assert.Equal(0, slice.Open.Length);
    }

    [Fact]
    public void WalkForward_TinySeries_DoesNotThrow()
    {
        var cfg = new WalkForwardConfig { InSampleRatio = 0.7, WindowCount = 5 };
        var result = WalkForward.Run(
            MakeRule(50m, 50m, 1m, 50m),
            Ramp(3),
            RuleBuilder.ZeroCostOptions(),
            cfg,
            1000);

        Assert.NotNull(result);
        Assert.Equal(5, result.Windows.Count);
    }
}