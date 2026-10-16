using System.Globalization;
using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class IntegratedIndicatorTests
{
    private int assertionCount;

    internal void Run()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "integrated-indicators.csv"));
        List<decimal> prices = new List<decimal>();
        List<string[]> expected = new List<string[]>();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            prices.Add(decimal.Parse(cells[0], CultureInfo.InvariantCulture));
            expected.Add(cells);
        }
        VerifySeries(prices, expected, IndicatorFunction.ExponentialMovingAverage, 1);
        VerifySeries(prices, expected, IndicatorFunction.SimpleMovingAverage, 2);
        VerifySeries(prices, expected, IndicatorFunction.RelativeStrengthIndex, 3);
        VerifyVersionsAndCache(prices);
        VerifyEventsAndCoordinator(prices);
        Console.WriteLine("Passed " + this.assertionCount + " integrated indicator assertions.");
    }

    private void VerifySeries(List<decimal> prices, List<string[]> expected, IndicatorFunction outerFunction, int column)
    {
        IndicatorValue inner = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 3);
        IndicatorValue outer = new IndicatorValue(outerFunction, "15 minute", 3, inner);
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices.ToArray());
        RuleMarketData full = CrossoverTestData.CreateMarket("15 minute", candles);
        List<decimal> prefix = new List<decimal>();
        for (int index = 0; index < prices.Count; index++)
        {
            prefix.Add(prices[index]);
            double value = double.Parse(expected[index][column], CultureInfo.InvariantCulture);
            double comparison = value;
            RuleStatus expectedStatus = RuleStatus.Matched;
            if (double.IsNaN(value))
            {
                comparison = 0.0;
                expectedStatus = RuleStatus.InsufficientData;
            }
            ComparisonCondition condition = new ComparisonCondition(outer, ComparisonOperator.Equal, new NumberValue(comparison));
            BoundRule bound = RuleBinder.Bind(new RuleDefinition("Nested", "15 minute", condition));
            IndicatorCalculationCache cache = new IndicatorCalculationCache();
            BoundRuleExecution fullExecution = bound.BindData(full, cache, "fixture", IndicatorCalculationVersion.CompletedWarmupV1);
            RuleMarketData partial = CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, prefix.ToArray()));
            BoundRuleExecution prefixExecution = bound.BindData(partial, cache, "fixture", IndicatorCalculationVersion.CompletedWarmupV1);
            Assert(fullExecution.Evaluate(candles[index].ClosedAt).Status == expectedStatus, "Nested formula mismatch.");
            Assert(prefixExecution.Evaluate(candles[index].ClosedAt).Status == expectedStatus, "Prefix replay differs.");
        }
    }

    private void VerifyVersionsAndCache(List<decimal> prices)
    {
        IndicatorValue ema = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 3);
        ComparisonCondition comparison = new ComparisonCondition(ema, ComparisonOperator.GreaterThan, new NumberValue(0));
        BoundRule rule = RuleBinder.Bind(new RuleDefinition("Version", "15 minute", comparison));
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices.ToArray());
        RuleMarketData data = CrossoverTestData.CreateMarket("15 minute", candles);
        IndicatorCalculationCache cache = new IndicatorCalculationCache();
        Assert(rule.BindData(data).Evaluate(candles[0].ClosedAt).Status == RuleStatus.Matched, "Legacy default changed.");
        BoundRuleExecution modern = rule.BindData(data, cache, "v1", IndicatorCalculationVersion.CompletedWarmupV1);
        Assert(modern.Evaluate(candles[0].ClosedAt).Status == RuleStatus.InsufficientData, "Modern EMA has no warm-up.");
        modern.Evaluate(candles[6].ClosedAt);
        Assert(cache.Count == 1, "Unrequested indicators were calculated.");
        rule.BindData(data, cache, "v1", IndicatorCalculationVersion.CompletedWarmupV1).Evaluate(candles[6].ClosedAt);
        Assert(cache.Count == 1, "Same snapshot was not reused.");
        rule.BindData(data, cache, "v1", IndicatorCalculationVersion.LegacyV1).Evaluate(candles[6].ClosedAt);
        Assert(cache.Count == 2, "Calculation versions share a cache key.");
        rule.BindData(data, cache, "v2", IndicatorCalculationVersion.CompletedWarmupV1).Evaluate(candles[6].ClosedAt);
        Assert(cache.Count == 3, "Revision change did not invalidate.");
        List<decimal> revisedPrices = new List<decimal>(prices);
        revisedPrices[0] = 20;
        RuleMarketData revised = CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, revisedPrices.ToArray()));
        rule.BindData(revised, cache, "v1", IndicatorCalculationVersion.CompletedWarmupV1).Evaluate(candles[6].ClosedAt);
        Assert(cache.Count == 4, "Changed data reused stale values with the same revision label.");
        Assert(data.Fingerprint != revised.Fingerprint, "Fingerprint missed changed prices.");
        InstrumentCandleData passive = new InstrumentCandleData(1, "day", new Candle[0]);
        Assert(passive.PivotPoints == null && passive.IndicatorData != null && passive.IndicatorData.Count == 0, "Candle container performed calculations.");
    }

    private void VerifyEventsAndCoordinator(List<decimal> prices)
    {
        IndicatorValue inner = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 3);
        IndicatorValue outer = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 3, inner);
        ComparisonCondition crossing = new ComparisonCondition(outer, ComparisonOperator.CrossedAbove, new NumberValue(3.5));
        RuleDefinition definition = new RuleDefinition("Modern cross", "15 minute", crossing);
        BoundRule bound = RuleBinder.Bind(definition);
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices.ToArray());
        RuleMarketData data = CrossoverTestData.CreateMarket("15 minute", candles);
        IndicatorCalculationCache cache = new IndicatorCalculationCache();
        RuleCrossoverMonitor monitor = bound.CreateCrossoverMonitor(IndicatorCalculationVersion.CompletedWarmupV1, "fixture", cache);
        CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        monitor.Process(data, candles[5].ClosedAt.AddTicks(-1));
        Assert(collector.Detected.Count == 0, "Modern cross fired before close.");
        monitor.Process(data, candles[5].ClosedAt);
        Assert(collector.Detected.Count == 1, "Modern cross was not discovered.");
        Assert(collector.Detected[0].Previous.Left.Value == 3.0 && collector.Detected[0].Current.Left.Value == 4.0, "Event used the wrong indicator version.");
        Assert(collector.Detected[0].Next == null, "Future next candle leaked.");
        monitor.Process(data, candles[5].ClosedAt);
        Assert(collector.Detected.Count == 1, "Duplicate event emitted.");
        monitor.Process(data, candles[6].ClosedAt);
        Assert(collector.Completed.Count == 1 && collector.Completed[0].EventId == collector.Detected[0].EventId, "Next-candle update lost event identity.");
        IndicatorDependencyPlan plan = new StrategyDependencyCollector().Collect(definition);
        DemandDrivenIndicatorExecutor executor = new DemandDrivenIndicatorExecutor(plan,
            new BasicIndicatorSeriesCalculator(new CandleSourceSeriesReader()), new IndicatorCalculationCache(), 1, "CompletedWarmupV1", "fixture");
        IReadOnlyDictionary<IndicatorDependency, double[]> calculated = executor.Calculate(data);
        Assert(calculated.Count == 2, "Coordinator lost nested dependencies.");
        Assert(calculated[plan.Indicators[1]][6] == 5.0, "Coordinator did not calculate the nested outer EMA.");
    }

    private void Assert(bool condition, string message)
    {
        this.assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
