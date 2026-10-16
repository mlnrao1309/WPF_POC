using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class StrategyDependencyTests
{
    private int assertionCount;

    internal void Run()
    {
        IndicatorValue inner = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 5,
            new CandleValue("15 minute", CandleField.Close));
        IndicatorValue outer = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 5, inner);
        ComparisonCondition first = new ComparisonCondition(outer, ComparisonOperator.GreaterThan, new NumberValue(100));
        IndicatorValue duplicate = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 5, inner);
        ComparisonCondition second = new ComparisonCondition(duplicate, ComparisonOperator.LessThan, new NumberValue(200));
        List<ConditionDefinition> children = new List<ConditionDefinition>();
        children.Add(first);
        children.Add(second);
        ConditionGroup group = new ConditionGroup(GroupOperator.All, children);
        RuleDefinition definition = new RuleDefinition("Nested EMA strategy", "15 minute", group);
        IndicatorDependencyPlan plan = new StrategyDependencyCollector().Collect(definition);
        Assert(plan.Indicators.Count == 2, "Nested EMA should produce inner and outer dependencies once each.");
        Assert(plan.Indicators[0].Function == IndicatorFunction.ExponentialMovingAverage
            && plan.Indicators[1].Function == IndicatorFunction.ExponentialMovingAverage, "Dependency function was changed.");
        Assert(plan.Indicators[0].Source is CandleValue, "The inner dependency must retain its candle source.");
        Assert(plan.Indicators[1].Source is IndicatorValue, "The outer dependency must retain its nested source.");
        Assert(plan.RequiredTimeframes.Count == 1 && plan.RequiredTimeframes[0] == "15 minute", "Duplicate timeframes must be deduplicated.");

        IndicatorValue dailyRsi = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "D", 14,
            new CandleValue("D", CandleField.Low));
        ComparisonCondition dailyCondition = new ComparisonCondition(dailyRsi, ComparisonOperator.GreaterThan, new NumberValue(50));
        List<ConditionDefinition> mixedChildren = new List<ConditionDefinition>();
        mixedChildren.Add(group);
        mixedChildren.Add(dailyCondition);
        ConditionGroup mixed = new ConditionGroup(GroupOperator.All, mixedChildren);
        IndicatorDependencyPlan mixedPlan = new StrategyDependencyCollector().Collect(
            new RuleDefinition("Mixed strategy", "15 minute", mixed));
        Assert(mixedPlan.Indicators.Count == 3, "Mixed nested strategy dependencies were not expanded.");
        Assert(mixedPlan.RequiredTimeframes.Count == 2, "Mixed strategy timeframes were not retained.");
        Assert(ContainsFunction(mixedPlan, IndicatorFunction.RelativeStrengthIndex), "Declared RSI dependency was lost.");
        Assert(!ContainsFunction(plan, IndicatorFunction.RelativeStrengthIndex), "Undeclared RSI must not be calculated.");
        Assert(new StrategyDependencyCollector().Collect(new RuleDefinition("Candle only", "15 minute",
            new ComparisonCondition(new CandleValue("15 minute"), ComparisonOperator.GreaterThan, new NumberValue(1)))).Indicators.Count == 0,
            "Candle-only rule must not request indicators.");
        Console.WriteLine("Passed " + assertionCount + " strategy dependency assertions.");
    }

    private bool ContainsFunction(IndicatorDependencyPlan plan, IndicatorFunction function)
    {
        foreach (IndicatorDependency dependency in plan.Indicators)
        {
            if (dependency.Function == function)
            {
                return true;
            }
        }
        return false;
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
