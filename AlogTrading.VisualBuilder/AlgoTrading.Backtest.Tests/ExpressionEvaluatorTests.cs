using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class ExpressionEvaluatorTests
{
    private static (ExpressionEvaluator eval, TimeframeAccessor acc) Build(
        IBarSeries series, Dictionary<string, decimal>? p = null)
    {
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(series.InstrumentToken, series, cache);
        var provider = new IndicatorProvider(acc);
        var eval = new ExpressionEvaluator(acc, provider, p ?? new Dictionary<string, decimal>());
        return (eval, acc);
    }

    [Fact]
    public void NumberExpression_ReturnsValue()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m, 101m);
        var (eval, _) = Build(series);
        var expr = new NumberExpression { Value = 42m };

        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out var v));
        Assert.Equal(42m, v);
    }

    [Fact]
    public void FieldExpression_CurrentClose()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m, 101m, 102m);
        var (eval, _) = Build(series);
        var expr = new FieldExpression { Field = Field.Close, Offset = 0 };

        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 2, 0, out var v));
        Assert.Equal(102m, v);
    }

    [Fact]
    public void FieldExpression_WithOffset1_ReturnsPrevious()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m, 101m, 102m);
        var (eval, _) = Build(series);
        var expr = new FieldExpression { Field = Field.Close, Offset = 1 };

        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 2, 0, out var v));
        Assert.Equal(101m, v);
    }

    [Fact]
    public void FieldExpression_AtStartOfData_ReturnsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m, 101m);
        var (eval, _) = Build(series);
        var expr = new FieldExpression { Field = Field.Close, Offset = 1 };

        Assert.False(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out _));
    }

    [Fact]
    public void ParameterExpression_ResolvesFromDictionary()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m);
        var p = new Dictionary<string, decimal> { ["thr"] = 55m };
        var (eval, _) = Build(series, p);
        var expr = new ParameterExpression { Name = "thr" };

        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out var v));
        Assert.Equal(55m, v);
    }

    [Fact]
    public void ParameterExpression_MissingName_ReturnsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m);
        var (eval, _) = Build(series);
        var expr = new ParameterExpression { Name = "nope" };

        Assert.False(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out _));
    }

    [Fact]
    public void CalculationExpression_Add()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m);
        var (eval, _) = Build(series);
        var expr = new CalculationExpression
        {
            Op = CalculationOp.Add,
            Left = new NumberExpression { Value = 10m },
            Right = new NumberExpression { Value = 5m }
        };

        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out var v));
        Assert.Equal(15m, v);
    }

    [Fact]
    public void CalculationExpression_DivideByZero_ReturnsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("15m"),
            new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc), 100m);
        var (eval, _) = Build(series);
        var expr = new CalculationExpression
        {
            Op = CalculationOp.Divide,
            Left = new NumberExpression { Value = 10m },
            Right = new NumberExpression { Value = 0m }
        };

        Assert.False(eval.TryEvaluate(expr, Timeframe.Parse("15m"), 0, 0, out _));
    }

    [Fact]
    public void IndicatorExpression_Ema_AfterWarmup()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Enumerable.Range(1, 30).Select(i => (decimal)i).ToArray());
        var (eval, _) = Build(series);
        var expr = new IndicatorExpression
        {
            Indicator = IndicatorKind.Ema,
            Source = new FieldExpression { Field = Field.Close, Offset = 0 },
            Params = new Dictionary<string, decimal> { ["period"] = 10m }
        };

        // period 10 â†’ warmup 9, so at bar index 20, valid value exists
        Assert.True(eval.TryEvaluate(expr, Timeframe.Parse("D"), 20, 0, out var v));
        Assert.True(v > 0m);
    }
}