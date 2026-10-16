using AlgoTrading.Models.Rules;

internal sealed class DatabaseCrossoverRuleFactory
{
    internal RuleDefinition Create(DatabaseCrossoverSettings settings)
    {
        var close = new CandleValue(settings.Timeframe, CandleField.Close);
        var exponentialAverage = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, settings.Timeframe, 5, close);
        var nestedExponentialAverage = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, settings.Timeframe, 5, exponentialAverage);
        var superTrend = new IndicatorValue(IndicatorFunction.SuperTrend, settings.Timeframe, 10, Multiplier: 3);
        var previousHigh = new CandleValue(settings.Timeframe, CandleField.High, 1);
        var conditions = new List<ConditionDefinition>
        {
            new ComparisonCondition(exponentialAverage, ComparisonOperator.CrossedAbove, nestedExponentialAverage),
            new ComparisonCondition(close, ComparisonOperator.GreaterThan, superTrend),
            new ComparisonCondition(close, ComparisonOperator.GreaterThan, exponentialAverage),
            new ComparisonCondition(close, ComparisonOperator.GreaterThan, previousHigh)
        };
        return new RuleDefinition("Instrument 265 fifteen-minute nested exponential-average crossover", settings.Timeframe,
            new ConditionGroup(GroupOperator.All, conditions.AsReadOnly()));
    }
}
