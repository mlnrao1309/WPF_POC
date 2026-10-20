using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class IndicatorProviderTests
{
    private static IndicatorProvider Build(IBarSeries series)
    {
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(series.InstrumentToken, series, cache);
        return new IndicatorProvider(acc);
    }

    [Fact]
    public void Sma_ProducesExpectedCount()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray());
        var provider = Build(series);
        var expr = new IndicatorExpression
        {
            Indicator = IndicatorKind.Sma,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 5m }
        };

        var result = provider.Resolve(expr, Timeframe.Parse("D"));
        Assert.Single(result.OutputNames);
        Assert.Equal("Value", result.OutputNames[0]);
        Assert.Equal(1, result.Outputs.Length);
        Assert.Equal(20, result.Outputs[0].Length);
    }

    [Fact]
    public void SameExpression_ReturnsCachedInstance()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray());
        var provider = Build(series);
        var expr = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 5m }
        };

        var a = provider.Resolve(expr, Timeframe.Parse("D"));
        var b = provider.Resolve(expr, Timeframe.Parse("D"));
        Assert.Same(a, b);
    }

    [Fact]
    public void DifferentPeriods_ProduceDifferentSeries()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray());
        var provider = Build(series);

        var e5 = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 5m }
        };
        var e10 = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 10m }
        };

        Assert.NotSame(provider.Resolve(e5, Timeframe.Parse("D")),
                       provider.Resolve(e10, Timeframe.Parse("D")));
    }

    [Fact]
    public void NestedEma_ProducesSeries()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 40).Select(i => (decimal)i).ToArray());
        var provider = Build(series);

        var innerEma = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 10m }
        };
        var outerEma = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = innerEma,
            Params = new Dictionary<string, decimal> { ["period"] = 10m }
        };

        var result = provider.Resolve(outerEma, Timeframe.Parse("D"));
        Assert.Equal(40, result.Outputs[0].Length);
        Assert.True(result.FirstValidIndex >= 18);  // 9 + 9
    }

    [Fact]
    public void MultiOutputIndicator_ReturnsAllOutputs()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 40).Select(i => (decimal)i).ToArray());
        var provider = Build(series);
        var expr = new IndicatorExpression
        {
            Indicator = IndicatorKind.Macd,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal>
            {
                ["fast"] = 12m, ["slow"] = 26m, ["signal"] = 9m
            }
        };

        var result = provider.Resolve(expr, Timeframe.Parse("D"));
        Assert.Equal(3, result.Outputs.Length);
        Assert.Contains("Macd", result.OutputNames);
        Assert.Contains("Signal", result.OutputNames);
        Assert.Contains("Histogram", result.OutputNames);
    }
}