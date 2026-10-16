using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class IndicatorCacheTests
{
    private int assertionCount;

    internal void Run()
    {
        IndicatorValue ema = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 5,
            new CandleValue("15 minute", CandleField.Close));
        IndicatorValue rsi = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "15 minute", 14,
            new CandleValue("15 minute", CandleField.Low));
        List<ConditionDefinition> children = new List<ConditionDefinition>();
        children.Add(new ComparisonCondition(ema, ComparisonOperator.GreaterThan, new NumberValue(50)));
        children.Add(new ComparisonCondition(rsi, ComparisonOperator.GreaterThan, new NumberValue(50)));
        ConditionGroup group = new ConditionGroup(GroupOperator.All, children);
        IndicatorDependencyPlan plan = new StrategyDependencyCollector().Collect(new RuleDefinition("Cache test", "15 minute", group));
        RuleMarketData market = IndicatorFixtureReader.ReadMarket("basic-indicator-candles.csv");
        IndicatorCalculationCache cache = new IndicatorCalculationCache();
        CountingIndicatorCalculator calculator = new CountingIndicatorCalculator();
        DemandDrivenIndicatorExecutor first = new DemandDrivenIndicatorExecutor(plan, calculator, cache, 1, "ema-rsi-v1", "revision-a");
        IReadOnlyDictionary<IndicatorDependency, double[]> firstValues = first.Calculate(market);
        Assert(firstValues.Count == 2 && calculator.CalculationCount == 2, "Declared indicators were not calculated exactly once.");
        firstValues[plan.Indicators[0]][0] = 999;
        IReadOnlyDictionary<IndicatorDependency, double[]> secondValues = first.Calculate(market);
        Assert(secondValues.Count == 2 && calculator.CalculationCount == 2, "Cache did not reuse the same data revision.");
        Assert(secondValues[plan.Indicators[0]][0] != 999, "Cached arrays were exposed for mutation.");
        DemandDrivenIndicatorExecutor revised = new DemandDrivenIndicatorExecutor(plan, calculator, cache, 1, "ema-rsi-v1", "revision-b");
        revised.Calculate(market);
        Assert(calculator.CalculationCount == 4, "A new data revision reused stale indicator values.");
        IndicatorDependencyPlan emaOnly = new StrategyDependencyCollector().Collect(new RuleDefinition("EMA only", "15 minute",
            new ComparisonCondition(ema, ComparisonOperator.GreaterThan, new NumberValue(50))));
        DemandDrivenIndicatorExecutor onlyEma = new DemandDrivenIndicatorExecutor(emaOnly, calculator, cache, 1, "ema-rsi-v1", "revision-c");
        onlyEma.Calculate(market);
        Assert(calculator.CalculationCount == 5, "Undeclared RSI was calculated.");
        Console.WriteLine("Passed " + assertionCount + " indicator cache assertions.");
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
