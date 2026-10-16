using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class BasicIndicatorCalculatorTests
{
    private int assertionCount;

    internal void Run()
    {
        IndicatorValue smaValue = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 3,
            new CandleValue("15 minute", CandleField.Close));
        IndicatorValue emaValue = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 3,
            new CandleValue("15 minute", CandleField.Close));
        IndicatorValue rsiValue = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "15 minute", 3,
            new CandleValue("15 minute", CandleField.Close));
        IndicatorValue upperBandValue = new IndicatorValue(IndicatorFunction.BollingerUpperBand, "15 minute", 3,
            new CandleValue("15 minute", CandleField.Close), 2.0);
        IndicatorValue lowerBandValue = new IndicatorValue(IndicatorFunction.BollingerLowerBand, "15 minute", 3,
            new CandleValue("15 minute", CandleField.Close), 2.0);
        List<ConditionDefinition> children = new List<ConditionDefinition>();
        children.Add(new ComparisonCondition(smaValue, ComparisonOperator.GreaterThan, new NumberValue(0)));
        children.Add(new ComparisonCondition(emaValue, ComparisonOperator.GreaterThan, new NumberValue(0)));
        children.Add(new ComparisonCondition(rsiValue, ComparisonOperator.GreaterThan, new NumberValue(0)));
        children.Add(new ComparisonCondition(upperBandValue, ComparisonOperator.GreaterThan, new NumberValue(0)));
        children.Add(new ComparisonCondition(lowerBandValue, ComparisonOperator.GreaterThan, new NumberValue(0)));
        ConditionGroup group = new ConditionGroup(GroupOperator.All, children);
        RuleDefinition definition = new RuleDefinition("Indicator calculation", "15 minute", group);
        IndicatorDependencyPlan plan = new StrategyDependencyCollector().Collect(definition);
        RuleMarketData market = CreateMarket();
        BasicIndicatorSeriesCalculator calculator = new BasicIndicatorSeriesCalculator(new CandleSourceSeriesReader());
        IndicatorDependency sma = Find(plan, IndicatorFunction.SimpleMovingAverage);
        IndicatorDependency ema = Find(plan, IndicatorFunction.ExponentialMovingAverage);
        IndicatorDependency rsi = Find(plan, IndicatorFunction.RelativeStrengthIndex);
        IndicatorDependency upperBand = Find(plan, IndicatorFunction.BollingerUpperBand);
        IndicatorDependency lowerBand = Find(plan, IndicatorFunction.BollingerLowerBand);
        double[] smaSeries = calculator.Calculate(sma, market);
        double[] emaSeries = calculator.Calculate(ema, market);
        double[] rsiSeries = calculator.Calculate(rsi, market);
        double[] upperBandSeries = calculator.Calculate(upperBand, market);
        double[] lowerBandSeries = calculator.Calculate(lowerBand, market);
        Assert(double.IsNaN(smaSeries[1]) && Math.Abs(smaSeries[2] - 2.0) < 0.000001, "SMA warm-up or value is incorrect.");
        Assert(double.IsNaN(emaSeries[1]) && Math.Abs(emaSeries[2] - 2.0) < 0.000001, "EMA seed is incorrect.");
        Assert(Math.Abs(emaSeries[5] - 2.5) < 0.000001, "EMA continuation is incorrect.");
        Assert(Math.Abs(rsiSeries[3] - 66.6666666667) < 0.000001, "RSI initial value is incorrect.");
        Assert(Math.Abs(rsiSeries[5] - 80.9523809524) < 0.000001, "RSI continuation is incorrect.");
        Assert(Math.Abs(upperBandSeries[2] - 3.6329931619) < 0.000001, "Bollinger upper band is incorrect.");
        Assert(Math.Abs(lowerBandSeries[2] - 0.3670068381) < 0.000001, "Bollinger lower band is incorrect.");
        Console.WriteLine("Passed " + assertionCount + " basic indicator calculator assertions.");
    }

    private RuleMarketData CreateMarket()
    {
        return IndicatorFixtureReader.ReadMarket("basic-indicator-candles.csv");
    }

    private IndicatorDependency Find(IndicatorDependencyPlan plan, IndicatorFunction function)
    {
        foreach (IndicatorDependency dependency in plan.Indicators)
        {
            if (dependency.Function == function)
            {
                return dependency;
            }
        }
        throw new InvalidOperationException("The requested indicator dependency was not found.");
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
