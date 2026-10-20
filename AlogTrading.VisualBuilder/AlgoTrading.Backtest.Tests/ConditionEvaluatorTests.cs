using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public class ConditionEvaluatorTests
{
    private static (ConditionEvaluator cond, TimeframeAccessor acc) Build(IBarSeries series)
    {
        var cache = new TimeframeCache(series);
        var acc = new TimeframeAccessor(series.InstrumentToken, series, cache);
        var provider = new IndicatorProvider(acc);
        var eval = new ExpressionEvaluator(acc, provider, new Dictionary<string, decimal>());
        return (new ConditionEvaluator(eval), acc);
    }

    private static FieldExpression Close(int offset = 0)
        => new() { Field = Field.Close, Offset = offset };

    private static NumberExpression Num(decimal v) => new() { Value = v };

    [Fact]
    public void SimpleGreaterThan_True()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m, 150m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.GreaterThan,
            Right = Num(120m)
        };

        Assert.True(cond.Evaluate(c, Timeframe.Parse("D"), 1));
    }

    [Fact]
    public void SimpleGreaterThan_False()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m, 90m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.GreaterThan,
            Right = Num(95m)
        };

        Assert.False(cond.Evaluate(c, Timeframe.Parse("D"), 1));
    }

    [Fact]
    public void DisabledCondition_IsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m, 150m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = false,
            Left = Close(),
            Operator = Operator.GreaterThan,
            Right = Num(120m)
        };

        Assert.False(cond.Evaluate(c, Timeframe.Parse("D"), 1));
    }

    [Fact]
    public void CrossedAbove_True()
    {
        // Close: 90, 100, 110. Number: 100. CrossedAbove at bar 2.
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 90m, 100m, 110m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.CrossedAbove,
            Right = Num(105m)
        };

        Assert.True(cond.Evaluate(c, Timeframe.Parse("D"), 2));
    }

    [Fact]
    public void CrossedAbove_AlreadyAbove_IsFalse()
    {
        // Close: 100, 110, 120. Number: 105. Already above at bar 1 â†’ bar 2 is not a cross.
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m, 110m, 120m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.CrossedAbove,
            Right = Num(105m)
        };

        Assert.False(cond.Evaluate(c, Timeframe.Parse("D"), 2));
    }

    [Fact]
    public void CrossedAbove_AtStartOfData_IsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 90m, 110m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.CrossedAbove,
            Right = Num(100m)
        };

        Assert.False(cond.Evaluate(c, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void CrossedBelow_True()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 110m, 100m, 90m);
        var (cond, _) = Build(series);
        var c = new Condition
        {
            Enabled = true,
            Left = Close(),
            Operator = Operator.CrossedBelow,
            Right = Num(95m)
        };

        Assert.True(cond.Evaluate(c, Timeframe.Parse("D"), 2));
    }

    [Fact]
    public void GroupAll_Empty_IsTrue()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var g = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.All,
            Children = new List<Node>()
        };

        Assert.True(cond.Evaluate(g, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void GroupAny_Empty_IsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var g = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.Any,
            Children = new List<Node>()
        };

        Assert.False(cond.Evaluate(g, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void GroupAll_WithDisabledChild_SkipsIt()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var g = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.All,
            Children = new List<Node>
            {
                new Condition { Enabled = false, Left = Close(), Operator = Operator.GreaterThan, Right = Num(999m) },
                new Condition { Enabled = true,  Left = Close(), Operator = Operator.LessThan,    Right = Num(200m) }
            }
        };

        Assert.True(cond.Evaluate(g, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void GroupAny_OneTrue_IsTrue()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var g = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.Any,
            Children = new List<Node>
            {
                new Condition { Enabled = true, Left = Close(), Operator = Operator.GreaterThan, Right = Num(999m) },
                new Condition { Enabled = true, Left = Close(), Operator = Operator.LessThan,    Right = Num(200m) }
            }
        };

        Assert.True(cond.Evaluate(g, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void GroupAny_AllFalse_IsFalse()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var g = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.Any,
            Children = new List<Node>
            {
                new Condition { Enabled = true, Left = Close(), Operator = Operator.GreaterThan, Right = Num(999m) },
                new Condition { Enabled = true, Left = Close(), Operator = Operator.LessThan,    Right = Num(50m) }
            }
        };

        Assert.False(cond.Evaluate(g, Timeframe.Parse("D"), 0));
    }

    [Fact]
    public void NestedGroup_AllInsideAny()
    {
        var series = TestSeries.FromCloses(Timeframe.Parse("D"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m);
        var (cond, _) = Build(series);
        var inner = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.All,
            Children = new List<Node>
            {
                new Condition { Enabled = true, Left = Close(), Operator = Operator.GreaterThan, Right = Num(50m) },
                new Condition { Enabled = true, Left = Close(), Operator = Operator.LessThan,    Right = Num(200m) }
            }
        };
        var outer = new ConditionGroup
        {
            Enabled = true,
            Logic = GroupLogic.Any,
            Children = new List<Node>
            {
                new Condition { Enabled = true, Left = Close(), Operator = Operator.GreaterThan, Right = Num(999m) },
                inner
            }
        };

        Assert.True(cond.Evaluate(outer, Timeframe.Parse("D"), 0));
    }
}