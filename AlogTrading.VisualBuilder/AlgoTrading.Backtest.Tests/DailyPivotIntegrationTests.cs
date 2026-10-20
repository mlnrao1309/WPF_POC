using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class DailyPivotIntegrationTests
{
    [Fact]
    public void ClassicPivot_UsesDailyCandlesAndProjectsOntoIndicatorTimeframe()
    {
        var intraday = CreateIntradaySeries();
        var daily = CreateDailySeries();
        var cache = new TimeframeCache(intraday, daily);
        var accessor = new TimeframeAccessor(intraday.InstrumentToken, intraday, cache);
        var provider = new IndicatorProvider(accessor);
        var expression = new IndicatorExpression
        {
            Indicator = IndicatorKind.PivotPointsClassic,
            Timeframe = Timeframe.Parse("15m"),
            Output = "PP"
        };

        var result = provider.Resolve(expression, Timeframe.Parse("15m"));
        int ppIndex = result.IndexOfOutput("PP");

        Assert.Equal(4, result.Outputs[ppIndex].Length);
        Assert.Equal(105m, result.Outputs[ppIndex][0]);
        Assert.Equal(105m, result.Outputs[ppIndex][1]);
        Assert.Equal(115m, result.Outputs[ppIndex][2]);
        Assert.Equal(115m, result.Outputs[ppIndex][3]);
    }

    [Fact]
    public void OrdinaryIndicator_UsesExplicitOrEvaluationTimeframe()
    {
        var intraday = CreateIntradaySeries();
        var daily = CreateDailySeries();
        var cache = new TimeframeCache(intraday, daily);
        var accessor = new TimeframeAccessor(intraday.InstrumentToken, intraday, cache);
        var provider = new IndicatorProvider(accessor);

        var evaluationExpression = CreateSmaExpression(null);
        var dailyExpression = CreateSmaExpression(Timeframe.Parse("D"));

        var evaluationResult = provider.Resolve(evaluationExpression, Timeframe.Parse("15m"));
        var dailyResult = provider.Resolve(dailyExpression, Timeframe.Parse("15m"));

        Assert.Equal(intraday.Count, evaluationResult.Outputs[0].Length);
        Assert.Equal(daily.Count, dailyResult.Outputs[0].Length);
    }

    private static IndicatorExpression CreateSmaExpression(Timeframe? timeframe)
    {
        return new IndicatorExpression
        {
            Indicator = IndicatorKind.Sma,
            Timeframe = timeframe,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 2m }
        };
    }

    private static TestSeries CreateIntradaySeries()
    {
        return new TestSeries
        {
            Timeframe = Timeframe.Parse("15m"),
            Open = new decimal[] { 1m, 1m, 1m, 1m },
            High = new decimal[] { 2m, 2m, 2m, 2m },
            Low = new decimal[] { 0m, 0m, 0m, 0m },
            Close = new decimal[] { 1m, 1m, 1m, 1m },
            Volume = new decimal[] { 100m, 100m, 100m, 100m },
            OI = new decimal[] { 0m, 0m, 0m, 0m },
            DateTime = new DateTime[]
            {
                Utc(2024, 1, 3, 3, 45),
                Utc(2024, 1, 3, 4, 0),
                Utc(2024, 1, 4, 3, 45),
                Utc(2024, 1, 4, 4, 0)
            }
        };
    }

    private static TestSeries CreateDailySeries()
    {
        return new TestSeries
        {
            Timeframe = Timeframe.Parse("D"),
            Open = new decimal[] { 104m, 114m, 124m },
            High = new decimal[] { 110m, 120m, 130m },
            Low = new decimal[] { 100m, 110m, 120m },
            Close = new decimal[] { 105m, 115m, 125m },
            Volume = new decimal[] { 1000m, 1100m, 1200m },
            OI = new decimal[] { 0m, 0m, 0m },
            DateTime = new DateTime[]
            {
                Utc(2024, 1, 2, 3, 45),
                Utc(2024, 1, 3, 3, 45),
                Utc(2024, 1, 4, 3, 45)
            }
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }
}
