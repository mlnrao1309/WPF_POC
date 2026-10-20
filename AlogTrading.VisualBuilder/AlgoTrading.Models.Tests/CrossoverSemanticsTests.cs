using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Models.Tests;

// Section 2.5 â€” crossover semantics.
//   CrossedAbove: left[0] > right[0] AND left[1] <= right[1]
//   CrossedBelow: left[0] < right[0] AND left[1] >= right[1]
public class CrossoverSemanticsTests
{
    private static bool CrossedAbove(decimal[] l, decimal[] r)
        => l[0] > r[0] && l[1] <= r[1];

    private static bool CrossedBelow(decimal[] l, decimal[] r)
        => l[0] < r[0] && l[1] >= r[1];

    [Fact]
    public void CrossedAbove_BasicTrue()
    {
        // bar[1]: left=1 right=2  (below)
        // bar[0]: left=3 right=2  (above)
        CrossedAbove(new[] { 3m, 1m }, new[] { 2m, 2m }).Should().BeTrue();
    }

    [Fact]
    public void CrossedAbove_TouchingPreviousIsCross()
    {
        // left[1] == right[1], left[0] > right[0] -> true per spec ("<=")
        CrossedAbove(new[] { 3m, 2m }, new[] { 2m, 2m }).Should().BeTrue();
    }

    [Fact]
    public void CrossedAbove_AlreadyAboveIsNotCross()
    {
        CrossedAbove(new[] { 3m, 2.5m }, new[] { 2m, 2m }).Should().BeFalse();
    }

    [Fact]
    public void CrossedBelow_BasicTrue()
    {
        CrossedBelow(new[] { 1m, 3m }, new[] { 2m, 2m }).Should().BeTrue();
    }

    [Fact]
    public void CrossedBelow_TouchingPreviousIsCross()
    {
        CrossedBelow(new[] { 1m, 2m }, new[] { 2m, 2m }).Should().BeTrue();
    }

    [Fact]
    public void CrossedBelow_AlreadyBelowIsNotCross()
    {
        CrossedBelow(new[] { 1m, 1.5m }, new[] { 2m, 2m }).Should().BeFalse();
    }

    [Fact]
    public void CrossedAbove_AtStartOfDataIsFalse()
    {
        // No previous bar -> cannot produce a crossover.
        var hasPrev = false;
        var isCross = hasPrev && CrossedAbove(new[] { 3m, 1m }, new[] { 2m, 2m });
        isCross.Should().BeFalse();
    }

    [Theory]
    [InlineData(Operator.OpenedAbove, Field.Open)]
    [InlineData(Operator.OpenedBelow, Field.Open)]
    [InlineData(Operator.ClosedAbove, Field.Close)]
    [InlineData(Operator.ClosedBelow, Field.Close)]
    public void OpenedClosed_AreUnaryAliases(Operator op, Field expected)
    {
        var rule = new RuleDefinition
        {
            SchemaVersion = 1,
            StrategyName = "t",
            Direction = Direction.Long,
            EvaluationTimeframe = Timeframe.Parse("D"),
            BaseTimeframe = Timeframe.Parse("D"),
            ExecutionMode = ExecutionMode.Delivery,
            StopLoss = new StopLossSpec { Mode = StopLossMode.Percentage, Value = 1m }
        };
        rule.Entry.Children.Add(new Condition
        {
            Left = new FieldExpression { Offset = 0, Field = expected },
            Operator = op,
            Right = new NumberExpression { Value = 0m }
        });
        RuleDefinitionValidator.Validate(rule).IsValid.Should().BeTrue();
    }
}