using AlgoTrading.Backtest;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class ParameterSweepTests
{
    private static RuleDefinition MakeThresholdRule(
        decimal min, decimal max, decimal step, decimal def = 50m)
    {
        return new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "SweepTest",
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

    private static IBarSeries Ramp(int n = 20, decimal start = 100m, decimal step = 5m)
    {
        var bars = new (decimal, decimal, decimal, decimal)[n];
        for (int i = 0; i < n; i++)
        {
            decimal p = start + step * i;
            bars[i] = (p, p + 1m, p - 1m, p);
        }
        return EngineTestSeries.FromOhlc(
            Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc),
            bars);
    }

    [Fact]
    public void GridSize_MinEqualsMax_OneValue()
    {
        var rule = MakeThresholdRule(50m, 50m, 1m, 50m);
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        Assert.Equal(1, result.CombinationCount);
        Assert.Single(result.Ranked);
    }

    [Fact]
    public void GridSize_PartialStep_StopsBeforeMax()
    {
        // Min=0, Max=10, Step=3 -> {0, 3, 6, 9} = 4 values.
        var rule = MakeThresholdRule(0m, 10m, 3m, 5m);
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        Assert.Equal(4, result.CombinationCount);
    }

    [Fact]
    public void GridSize_CartesianProduct()
    {
        // Two params: 3 values x 4 values = 12 combos.
        var rule = MakeThresholdRule(0m, 2m, 1m, 1m);
        rule.Parameters.Add(new ParameterSpec
        {
            Name = "unused", Default = 10m, Minimum = 0m, Maximum = 30m, Step = 10m
        });
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        Assert.Equal(12, result.CombinationCount);
    }

    [Fact]
    public void Cap_Exceeded_ThrowsArgumentException()
    {
        // 101 combos (0..100 step 1) > cap 100.
        var rule = MakeThresholdRule(0m, 100m, 1m, 50m);
        Assert.Throws<ArgumentException>(() => ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 100));
    }

    [Fact]
    public void NoParameters_SingleCombination()
    {
        var rule = RuleBuilder.AlwaysEntryLong();
        rule.Parameters = new List<ParameterSpec>();
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        Assert.Equal(1, result.CombinationCount);
        Assert.Empty(result.Best.Parameters);
    }

    [Fact]
    public void Best_IsRankedHead_And_HighestCriterion()
    {
        var rule = MakeThresholdRule(0m, 200m, 50m, 100m);
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        decimal max = result.Ranked.Max(e => e.CriterionValue);
        Assert.Equal(max, result.Best.CriterionValue);
        Assert.Same(result.Best, result.Ranked[0]);
    }

    [Fact]
    public void CriterionMapping_MatchesMetrics()
    {
        foreach (var c in new[]
        {
            SweepCriterion.Sharpe,
            SweepCriterion.ProfitFactor,
            SweepCriterion.NetPnL,
            SweepCriterion.Calmar
        })
        {
            var rule = MakeThresholdRule(50m, 50m, 1m, 50m);
            var result = ParameterSweep.Run(
                rule, Ramp(), RuleBuilder.ZeroCostOptions(), c, 1000);
            decimal expected = c switch
            {
                SweepCriterion.Sharpe       => result.Best.Metrics.Sharpe,
                SweepCriterion.ProfitFactor => result.Best.Metrics.ProfitFactor,
                SweepCriterion.NetPnL       => result.Best.Metrics.TotalNetPnL,
                SweepCriterion.Calmar       => result.Best.Metrics.Calmar,
                _ => 0m
            };
            Assert.Equal(expected, result.Best.CriterionValue);
        }
    }

    [Fact]
    public void ParametersActuallyVaryResults()
    {
        // thr=50 -> entry fires; thr=200 -> no entry. Criteria must differ.
        var rule = MakeThresholdRule(50m, 200m, 150m, 50m);
        var result = ParameterSweep.Run(
            rule, Ramp(), RuleBuilder.ZeroCostOptions(),
            SweepCriterion.NetPnL, 1000);
        int distinct = result.Ranked.Select(e => e.CriterionValue).Distinct().Count();
        Assert.True(distinct > 1,
            "Expected criterion values to differ across parameter combinations.");
    }
}