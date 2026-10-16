using System.Globalization;
using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class IndicatorRecoveryTests
{
    private int assertionCount;

    internal void Run()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "indicator-recovery.csv");
        string[] lines = File.ReadAllLines(path);
        List<decimal> prices = new List<decimal>();
        List<double> expected = new List<double>();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split(',');
            prices.Add(decimal.Parse(fields[0], CultureInfo.InvariantCulture));
            expected.Add(double.Parse(fields[1], CultureInfo.InvariantCulture));
        }

        BasicIndicatorSeriesCalculator calculator = new BasicIndicatorSeriesCalculator(new CandleSourceSeriesReader());
        IndicatorDependency implicitSource = CreateDependency(IndicatorFunction.RelativeStrengthIndex, null, 2.0);
        IndicatorDependency explicitSource = CreateDependency(IndicatorFunction.RelativeStrengthIndex, new CandleValue("15 minute", CandleField.Close), 2.0);
        RuleMarketData complete = CreateMarket(prices);
        double[] fullValues = calculator.Calculate(implicitSource, complete);
        double[] explicitValues = calculator.Calculate(explicitSource, complete);
        List<decimal> prefix = new List<decimal>();
        for (int index = 0; index < prices.Count; index++)
        {
            AssertEqual(expected[index], fullValues[index], "Fixture RSI");
            AssertEqual(explicitValues[index], fullValues[index], "Default source");
            prefix.Add(prices[index]);
            double[] prefixValues = calculator.Calculate(implicitSource, CreateMarket(prefix));
            AssertEqual(fullValues[index], prefixValues[index], "Prefix replay");
        }
        RejectMultiplier(calculator, complete, double.NaN);
        RejectMultiplier(calculator, complete, double.PositiveInfinity);
        RejectMultiplier(calculator, complete, double.NegativeInfinity);
        RejectMultiplier(calculator, complete, -1.0);
        RejectMultiplier(calculator, complete, 0.0);
        Console.WriteLine("Passed " + this.assertionCount + " indicator recovery assertions.");
    }

    private RuleMarketData CreateMarket(List<decimal> prices)
    {
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices.ToArray());
        return CrossoverTestData.CreateMarket("15 minute", candles);
    }

    private IndicatorDependency CreateDependency(IndicatorFunction function, ValueDefinition? source, double multiplier)
    {
        IndicatorValue indicator = new IndicatorValue(function, "15 minute", 3, source, multiplier);
        ComparisonCondition condition = new ComparisonCondition(indicator, ComparisonOperator.GreaterThan, new NumberValue(0));
        RuleDefinition definition = new RuleDefinition("Recovery", "15 minute", condition);
        StrategyDependencyCollector collector = new StrategyDependencyCollector();
        return collector.Collect(definition).Indicators[0];
    }

    private void RejectMultiplier(BasicIndicatorSeriesCalculator calculator, RuleMarketData market, double multiplier)
    {
        try
        {
            IndicatorDependency dependency = CreateDependency(IndicatorFunction.BollingerUpperBand, null, multiplier);
            calculator.Calculate(dependency, market);
        }
        catch (ArgumentException)
        {
            this.assertionCount++;
            return;
        }
        throw new InvalidOperationException("Invalid multiplier was accepted.");
    }

    private void AssertEqual(double expected, double actual, string context)
    {
        this.assertionCount++;
        if (double.IsNaN(expected) && double.IsNaN(actual))
        {
            return;
        }
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.00000001)
        {
            throw new InvalidOperationException(context + " differs from the expected value.");
        }
    }
}
