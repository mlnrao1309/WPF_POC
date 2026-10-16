using AlgoTrading.Models;
using AlgoTrading.Models.Rules;
using System.Text.Json;

internal sealed class RuleRegressionTests
{
    private int assertionCount;
    private readonly DateTimeOffset origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private CompletedCandle TrendCandle(int index, decimal high, decimal low, decimal close)
    {
        Candle candle = new Candle(1, "15 minute", origin.UtcDateTime, close, high, low, close, 0);
        return new CompletedCandle(candle, origin.AddMinutes(15 * (index + 1)));
    }

    private CompletedCandle[] Series(string timeframe, int intervalMinutes, params decimal[] closes)
    {
        CompletedCandle[] result = new CompletedCandle[closes.Length];
        for (int index = 0; index < closes.Length; index++)
        {
            decimal close = closes[index];
            Candle candle = new Candle(1, timeframe, origin.AddMinutes(index * intervalMinutes).UtcDateTime, close, close, close, close, 10);
            result[index] = new CompletedCandle(candle, origin.AddMinutes((index + 1) * intervalMinutes));
        }

        return result;
    }

    private RuleMarketData Market(params (string Timeframe, CompletedCandle[] Candles)[] series)
    {
        Dictionary<string, IReadOnlyList<CompletedCandle>> values = new Dictionary<string, IReadOnlyList<CompletedCandle>>();
        foreach ((string Timeframe, CompletedCandle[] Candles) entry in series)
        {
            values.Add(entry.Timeframe, entry.Candles);
        }

        return new RuleMarketData(1, values);
    }

    private BoundRule Bound(ConditionDefinition condition, string timeframe = "15 minute")
    {
        return RuleBinder.Bind(new RuleDefinition("Test rule", timeframe, condition));
    }

    private RuleStatus Status(ConditionDefinition condition, RuleMarketData data, int minutes, string timeframe = "15 minute")
    {
        return Bound(condition, timeframe).BindData(data).Evaluate(origin.AddMinutes(minutes)).Status;
    }

    private ComparisonCondition Equal(ValueDefinition left, double right)
    {
        return new ComparisonCondition(left, ComparisonOperator.Equal, new NumberValue(right));
    }

    internal void Run()
    {
        CandleValue closeValue = new CandleValue("15 minute");
        ComparisonCondition closeAboveTwo = new ComparisonCondition(closeValue, ComparisonOperator.GreaterThan, new NumberValue(2));
        ComparisonCondition crossedAboveTwo = new ComparisonCondition(closeValue, ComparisonOperator.CrossedAbove, new NumberValue(2));
        ComparisonCondition crossedBelowTwo = new ComparisonCondition(closeValue, ComparisonOperator.CrossedBelow, new NumberValue(2));
        RuleMarketData basicData = Market(("15 minute", Series("15 minute", 15, 1, 3, 2, 1, 4)));
        Assert(Status(crossedAboveTwo, basicData, 14) == RuleStatus.InsufficientData, "Cannot evaluate an unfinished candle");
        Assert(Status(crossedAboveTwo, basicData, 15) == RuleStatus.InsufficientData, "First crossover needs previous history");
        Assert(Status(crossedAboveTwo, basicData, 30) == RuleStatus.Matched, "Historical crossover uses current index");
        Assert(Status(crossedAboveTwo, basicData, 45) == RuleStatus.NotMatched, "Historical result must not use final array entries");
        Assert(Status(crossedBelowTwo, basicData, 60) == RuleStatus.Matched, "Cross below includes previous equality");
        Assert(Status(crossedAboveTwo, basicData, 75) == RuleStatus.Matched, "Cross above threshold");
        Assert(Bound(crossedAboveTwo).BindData(basicData).Evaluate(origin.AddMinutes(44)).EvaluatedAt == origin.AddMinutes(30), "Evaluation snaps to completed clock");
        IndicatorValue relativeStrength = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "15 minute", 2);
        IndicatorValue smoothedRelativeStrength = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 2, relativeStrength);
        RuleMarketData sampleData = Market(("15 minute", Series("15 minute", 15, 10, 12, 11, 13, 12)));
        Assert(Status(new ComparisonCondition(relativeStrength, ComparisonOperator.GreaterThan, new NumberValue(85.71)), sampleData, 60) == RuleStatus.Matched, "Relative strength uses normalized averages");
        Assert(Status(new ComparisonCondition(smoothedRelativeStrength, ComparisonOperator.GreaterThan, new NumberValue(76.19)), sampleData, 60) == RuleStatus.Matched, "Nested average recovers after indicator warmup");
        Assert(Status(Equal(smoothedRelativeStrength, 0), sampleData, 45) == RuleStatus.InsufficientData, "Nested warmup requires full valid window");
        ComparisonCondition relativeStrengthCross = new ComparisonCondition(relativeStrength, ComparisonOperator.CrossedAbove, new NumberValue(80));
        Assert(Status(relativeStrengthCross, sampleData, 60) == RuleStatus.Matched, "Indicator output crosses threshold, not source price");
        CompletedCandle[] lowCandles = Series("15 minute", 15, 10, 12, 11, 13);
        for (int index = 0; index < lowCandles.Length; index++)
        {
            Candle candle = lowCandles[index].Candle;
            lowCandles[index] = new CompletedCandle(new Candle(1, "15 minute", candle.Timestamp, candle.Open, candle.High, 4 - index, candle.Close, 10), lowCandles[index].ClosedAt);
        }

        IndicatorValue lowStrength = new IndicatorValue(relativeStrength.Function, relativeStrength.Timeframe, relativeStrength.Length, new CandleValue("15 minute", CandleField.Low), relativeStrength.Multiplier);
        Assert(Status(Equal(lowStrength, 0), Market(("15 minute", lowCandles)), 60) == RuleStatus.Matched, "Low source is respected");
        IndicatorValue fastAverage = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 20);
        IndicatorValue slowAverage = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "2 hour", 20);
        ComparisonCondition mixedCross = new ComparisonCondition(fastAverage, ComparisonOperator.CrossedAbove, slowAverage);
        decimal[] fastPrices = Enumerable.Repeat(9m, 168).ToArray();
        fastPrices[fastPrices.Length - 1] = 31;
        decimal[] slowPrices = Enumerable.Repeat(10m, 22).ToArray();
        slowPrices[slowPrices.Length - 1] = 10000;
        RuleMarketData mixedData = Market(("15 minute", Series("15 minute", 15, fastPrices)), ("2 hour", Series("2 hour", 120, slowPrices)));
        Assert(Status(mixedCross, mixedData, 2520) == RuleStatus.Matched, "Twenty-period mixed timeframe crossover aligns close times");
        Assert(Status(mixedCross, mixedData, 2519) == RuleStatus.NotMatched, "No partial fifteen-minute candle");
        slowPrices[20] = 50;
        RuleMarketData changedSlowData = Market(("15 minute", Series("15 minute", 15, fastPrices)), ("2 hour", Series("2 hour", 120, slowPrices)));
        Assert(Status(mixedCross, changedSlowData, 2520) == RuleStatus.NotMatched, "Simultaneous two-hour close participates");
        RuleMarketData truncatedData = Market(("15 minute", Series("15 minute", 15, fastPrices)), ("2 hour", Series("2 hour", 120, slowPrices.Take(21).ToArray())));
        Assert(Status(mixedCross, truncatedData, 2520) == Status(mixedCross, changedSlowData, 2520), "Future rows do not change historical results");
        ComparisonCondition unknown = Equal(new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 100), 0);
        Assert(Status(new NotCondition(unknown), basicData, 30) == RuleStatus.InsufficientData, "Not must not convert unavailable into match");
        Assert(Status(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(unknown, Equal(new NumberValue(1), 2))), basicData, 30) == RuleStatus.NotMatched, "False dominates unknown in all group");
        Assert(Status(new ConditionGroup(GroupOperator.Any, TestArray.Create<ConditionDefinition>(unknown, Equal(new NumberValue(1), 1))), basicData, 30) == RuleStatus.Matched, "True dominates unknown in any group");
        Assert(Status(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(RuleModelCompatibilityData.DisabledComparison(), closeAboveTwo)), basicData, 30) == RuleStatus.Matched, "Disabled condition is omitted");
        Assert(Status(new ConditionGroup(GroupOperator.Any, TestArray.Create<ConditionDefinition>(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(closeAboveTwo, crossedAboveTwo)), Equal(new NumberValue(0), 1))), basicData, 30) == RuleStatus.Matched, "Nested group meaning preserved");
        Assert(Status(Equal(new ArithmeticValue(new NumberValue(1), ArithmeticOperator.Divide, new NumberValue(0)), 1), basicData, 30) == RuleStatus.InsufficientData, "Division by zero is unavailable");
        Assert(Status(Equal(new RoundValue(new ArithmeticValue(new NumberValue(5), ArithmeticOperator.Divide, new NumberValue(2))), 3), basicData, 30) == RuleStatus.Matched, "Arithmetic brackets and rounding");
        Assert(Status(Equal(new PreviousValue(closeValue, "15 minute", 1), 1), basicData, 30) == RuleStatus.Matched, "Previous candle offset");
        Assert(Status(Equal(new CandleValue("15 minute", CandleField.Close, 1), 1), basicData, 30) == RuleStatus.Matched, "Direct candle offset");
        Assert(Status(Equal(new PreviousValue(closeValue, "15 minute", 10), 1), basicData, 30) == RuleStatus.InsufficientData, "Insufficient offset history");
        Assert(Status(Equal(new CountValue(closeAboveTwo, "15 minute", 3), 1), basicData, 45) == RuleStatus.Matched, "Count within full window");
        Assert(Status(Equal(new CountValue(crossedAboveTwo, "15 minute", 3), 1), basicData, 60) == RuleStatus.Matched, "Count of historical crossovers");
        Assert(Status(Equal(new CountValue(crossedAboveTwo, "15 minute", 3), 1), basicData, 45) == RuleStatus.InsufficientData, "Count does not treat unknown history as false");
        IndicatorValue upperBand = new IndicatorValue(IndicatorFunction.BollingerUpperBand, "15 minute", 2, Multiplier: 2);
        IndicatorValue lowerBand = new IndicatorValue(IndicatorFunction.BollingerLowerBand, upperBand.Timeframe, upperBand.Length, upperBand.Source, upperBand.Multiplier);
        Assert(Status(Equal(upperBand, 14), sampleData, 60) == RuleStatus.Matched, "Upper Bollinger output");
        Assert(Status(Equal(lowerBand, 10), sampleData, 60) == RuleStatus.Matched, "Lower Bollinger output");
        Assert(Status(Equal(new IndicatorValue(IndicatorFunction.BollingerMiddleBand, upperBand.Timeframe, upperBand.Length, upperBand.Source, upperBand.Multiplier), 12), sampleData, 60) == RuleStatus.Matched, "Middle Bollinger output");
        Assert(Status(Equal(new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 1), 13), sampleData, 60) == RuleStatus.Matched, "Exponential average length one");
        CompletedCandle[] trendCandles = new CompletedCandle[6];
        trendCandles[0] = TrendCandle(0, 12, 8, 10);
        trendCandles[1] = TrendCandle(1, 14, 10, 12);
        trendCandles[2] = TrendCandle(2, 13, 9, 11);
        trendCandles[3] = TrendCandle(3, 10, 6, 7);
        trendCandles[4] = TrendCandle(4, 11, 7, 9);
        trendCandles[5] = TrendCandle(5, 17, 13, 16);
        IndicatorValue superTrend = new IndicatorValue(IndicatorFunction.SuperTrend, "15 minute", 1, Multiplier: 1);
        double[] expectedTrend = TestArray.Create<double>(6, 8, 8, 13, 13, 7);
        for (int index = 0; index < expectedTrend.Length; index++)
        {
            Assert(Status(Equal(superTrend, expectedTrend[index]), Market(("15 minute", trendCandles)), (index + 1) * 15) == RuleStatus.Matched, "SuperTrend trailing bands and reversal");
        }

        IndicatorValue fullRelativeStrength = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "15 minute", 14);
        IndicatorValue fullSmoothedStrength = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 14, fullRelativeStrength);
        IndicatorValue fullUpperBand = new IndicatorValue(upperBand.Function, upperBand.Timeframe, 20, upperBand.Source, upperBand.Multiplier);
        ConditionDefinition[] requestedRules = TestArray.Create<ConditionDefinition>(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(new ComparisonCondition(fullRelativeStrength, ComparisonOperator.CrossedAbove, fullSmoothedStrength), new ComparisonCondition(fullRelativeStrength, ComparisonOperator.CrossedAbove, new NumberValue(61.8)), new ComparisonCondition(fullSmoothedStrength, ComparisonOperator.GreaterThanOrEqual, new NumberValue(50)))), new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(new ComparisonCondition(closeValue, ComparisonOperator.GreaterThan, fullUpperBand), new ComparisonCondition(fullRelativeStrength, ComparisonOperator.CrossedAbove, new NumberValue(50)))), new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(new ComparisonCondition(closeValue, ComparisonOperator.CrossedAbove, new IndicatorValue(superTrend.Function, superTrend.Timeframe, 7, superTrend.Source, 3)), new ComparisonCondition(closeValue, ComparisonOperator.GreaterThan, fullUpperBand), new ComparisonCondition(fullRelativeStrength, ComparisonOperator.GreaterThan, new NumberValue(50)))));
        for (int index = 0; index < requestedRules.Length; index++)
        {
            RuleDefinition definition = new RuleDefinition($"Requested example {index + 1}", "15 minute", requestedRules[index]);
            string saved = RuleDefinitionJson.Serialize(definition);
            RuleDefinition roundTripped = RuleDefinitionJson.Deserialize(saved);
            Assert(RuleBinder.Bind(roundTripped).Description == RuleBinder.Bind(definition).Description, "Saved rule round trip");
            Assert(RuleBinder.Bind(roundTripped).BindData(sampleData).Evaluate(origin.AddMinutes(60)).Status != RuleStatus.Matched, "Example cannot signal with insufficient history");
        }

        Assert(Bound(requestedRules[0]).Plan.IndicatorCalculations.Count == 2, "Shared nested indicators expanded once");
        Assert(Bound(mixedCross).Plan.RequiredTimeframes.Count == 2, "Required timeframe discovery");
        List<ConditionDefinition> editableChildren = new List<ConditionDefinition>();
        editableChildren.Add(closeAboveTwo);
        BoundRule frozen = Bound(new ConditionGroup(GroupOperator.All, editableChildren));
        editableChildren.Clear();
        Assert(frozen.BindData(basicData).Evaluate(origin.AddMinutes(30)).IsMatch, "Binding snapshots editable definitions");
        CompletedCandle[] mutableCandles = Series("15 minute", 15, 1, 3);
        RuleMarketData copiedMarket = Market(("15 minute", mutableCandles));
        mutableCandles[1] = mutableCandles[0];
        Assert(Status(crossedAboveTwo, copiedMarket, 30) == RuleStatus.Matched, "Market snapshot is immutable");
        bool rejected1 = false;
        try
        {
            Bound(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>()));
        }
        catch (ArgumentException)
        {
            rejected1 = true;
        }

        Assert(rejected1, "Empty groups rejected");
        bool rejected2 = false;
        try
        {
            Bound(Equal(new CandleValue("15 minute", CandlesAgo: -1), 0));
        }
        catch (ArgumentException)
        {
            rejected2 = true;
        }

        Assert(rejected2, "Future offsets rejected");
        bool rejected3 = false;
        try
        {
            Bound(Equal(new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 0), 0));
        }
        catch (ArgumentException)
        {
            rejected3 = true;
        }

        Assert(rejected3, "Zero period rejected");
        bool rejected4 = false;
        try
        {
            Bound(Equal(new IndicatorValue(IndicatorFunction.SuperTrend, "15 minute", 7, closeValue), 0));
        }
        catch (ArgumentException)
        {
            rejected4 = true;
        }

        Assert(rejected4, "Misleading SuperTrend source rejected");
        bool rejected5 = false;
        try
        {
            Bound(mixedCross).BindData(basicData);
        }
        catch (ArgumentException)
        {
            rejected5 = true;
        }

        Assert(rejected5, "Missing timeframe reported at binding");
        bool rejected6 = false;
        try
        {
            Market(("15 minute", TestArray.Create<CompletedCandle>(trendCandles[0], trendCandles[0])));
        }
        catch (ArgumentException)
        {
            rejected6 = true;
        }

        Assert(rejected6, "Duplicate close times rejected");
        bool rejected7 = false;
        try
        {
            Bound(Equal(new NumberValue(double.NaN), 0));
        }
        catch (ArgumentException)
        {
            rejected7 = true;
        }

        Assert(rejected7, "Nonfinite threshold rejected");
        bool rejected8 = false;
        try
        {
            Bound(new ComparisonCondition(closeValue, (ComparisonOperator)999, new NumberValue(0)));
        }
        catch (ArgumentException)
        {
            rejected8 = true;
        }

        Assert(rejected8, "Unknown operator rejected");
        // Every indicator must produce the same historical result with and without future rows.
        decimal[] variedPrices = new decimal[70];
        for (int index = 0; index < variedPrices.Length; index++)
        {
            variedPrices[index] = 100m + index % 9 * 3 - index % 4 * 2;
        }

        CompletedCandle[] allCandles = Series("15 minute", 15, variedPrices);
        foreach (IndicatorFunction function in Enum.GetValues<IndicatorFunction>())
        {
            IndicatorValue indicator = new IndicatorValue(function, "15 minute", 5, Multiplier: 2);
            ComparisonCondition comparison = new ComparisonCondition(indicator, ComparisonOperator.GreaterThan, new NumberValue(100));
            BoundRuleExecution completeExecution = Bound(comparison).BindData(Market(("15 minute", allCandles)));
            for (int candleCount = 10; candleCount <= 60; candleCount += 10)
            {
                BoundRuleExecution prefixExecution = Bound(comparison).BindData(Market(("15 minute", allCandles.Take(candleCount).ToArray())));
                System.DateTimeOffset timestamp = origin.AddMinutes(candleCount * 15);
                Assert(completeExecution.Evaluate(timestamp) == prefixExecution.Evaluate(timestamp), "Indicator must be causal: " + function);
            }
        }

        ComparisonCondition equalityAtCurrent = new ComparisonCondition(new NumberValue(62), ComparisonOperator.CrossedAbove, new NumberValue(62));
        Assert(Status(equalityAtCurrent, basicData, 30) == RuleStatus.NotMatched, "Current equality is not a crossover");
        Assert(Status(new ComparisonCondition(closeValue, ComparisonOperator.NotEqual, new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 100)), basicData, 30) == RuleStatus.InsufficientData, "NotEqual cannot match unavailable data");
        RuleMarketData emptyMarket = Market(("15 minute", Array.Empty<CompletedCandle>()));
        Assert(Status(closeAboveTwo, emptyMarket, 30) == RuleStatus.InsufficientData, "Empty completed series is unavailable");
        CompletedCandle[] differentOffsetCandles = Series("15 minute", 15, 1, 3);
        for (int index = 0; index < differentOffsetCandles.Length; index++)
        {
            CompletedCandle candle = differentOffsetCandles[index];
            differentOffsetCandles[index] = new CompletedCandle(candle.Candle, candle.ClosedAt.ToOffset(TimeSpan.FromHours(5.5)));
        }

        Assert(Status(crossedAboveTwo, Market(("15 minute", differentOffsetCandles)), 30) == RuleStatus.Matched, "Timestamp offsets compare the same instants");
        BoundRule staticFirst = Bound(new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(crossedAboveTwo, Equal(new NumberValue(5), 6))));
        Assert(staticFirst.BindData(basicData).Evaluate(origin.AddMinutes(45)).Explanation.Contains("5 equals 6"), "Ordinary comparisons evaluated first in all group");
        string malformedDocument = RuleDefinitionJson.Serialize(new RuleDefinition("Saved", "15 minute", closeAboveTwo)).Replace("\"Operator\": \"GreaterThan\",", "");
        bool malformedRejected = false;
        try
        {
            RuleDefinitionJson.Deserialize(malformedDocument);
        }
        catch (JsonException)
        {
            malformedRejected = true;
        }

        Assert(malformedRejected, "Missing saved operator cannot silently become greater-than");
        ConditionDefinition deeplyNested = closeAboveTwo;
        for (int depth = 0; depth < 60; depth++)
        {
            deeplyNested = new NotCondition(deeplyNested);
        }

        bool rejected9 = false;
        try
        {
            Bound(deeplyNested);
        }
        catch (ArgumentException)
        {
            rejected9 = true;
        }

        Assert(rejected9, "Excessive nesting rejected");
        // Compare the original requested rules against direct calculations with sufficient data.
        // Independent Wilder calculations and a direct moving window provide the expected first rule.
        decimal[] pricesForSignals = new decimal[500];
        for (int index = 0; index < pricesForSignals.Length; index++)
        {
            pricesForSignals[index] = 100m + (decimal)(20 * Math.Sin(index * 0.47) + 8 * Math.Sin(index * 1.11));
        }

        CompletedCandle[] signalCandles = Series("15 minute", 15, pricesForSignals);
        RuleMarketData signalMarket = Market(("15 minute", signalCandles));
        Candle[] sourceCandles = new Candle[signalCandles.Length];
        for (int index = 0; index < sourceCandles.Length; index++)
        {
            sourceCandles[index] = signalCandles[index].Candle;
        }

        InstrumentCandleData originalIndicatorData = new InstrumentCandleData(1, "15 minute", sourceCandles);
        originalIndicatorData.AddIndicatorData(Indicator.RSI, null, 14);
        originalIndicatorData.AddIndicatorData(Indicator.BollingerBands, null, 20, 2.0);
        originalIndicatorData.AddIndicatorData(Indicator.SuperTrend, null, 7, 3.0);
        double[] strengthValues = originalIndicatorData.IndicatorData!["RSI_Close_14"];
        double[] upperValues = originalIndicatorData.IndicatorData["BollingerBands_Close_20_2_Upper"];
        double[] trendValues = originalIndicatorData.IndicatorData["SuperTrend_HighLowClose_7_3"];
        double[] averageStrengthValues = new double[signalCandles.Length];
        for (int index = 0; index < averageStrengthValues.Length; index++)
        {
            averageStrengthValues[index] = double.NaN;
            if (index >= 27)
            {
                averageStrengthValues[index] = strengthValues.Skip(index - 13).Take(14).Average();
            }
        }

        BoundRuleExecution[] executions = new BoundRuleExecution[requestedRules.Length];
        for (int index = 0; index < executions.Length; index++)
        {
            executions[index] = Bound(requestedRules[index]).BindData(signalMarket);
        }

        for (int index = 28; index < signalCandles.Length; index++)
        {
            bool[] expected = TestArray.Create<bool>(strengthValues[index - 1] <= averageStrengthValues[index - 1] && strengthValues[index] > averageStrengthValues[index] && strengthValues[index - 1] <= 61.8 && strengthValues[index] > 61.8 && averageStrengthValues[index] >= 50, (double)pricesForSignals[index] > upperValues[index] && strengthValues[index - 1] <= 50 && strengthValues[index] > 50, (double)pricesForSignals[index - 1] <= trendValues[index - 1] && (double)pricesForSignals[index] > trendValues[index] && (double)pricesForSignals[index] > upperValues[index] && strengthValues[index] > 50);
            for (int exampleIndex = 0; exampleIndex < executions.Length; exampleIndex++)
            {
                Assert(executions[exampleIndex].Evaluate(signalCandles[index].ClosedAt).IsMatch == expected[exampleIndex], "Requested strategy agrees with direct calculation");
            }
        }

        decimal[] breakoutPrices = Enumerable.Repeat(100m, 30).Concat(TestArray.Create<decimal>(200m, 201m)).ToArray();
        RuleMarketData breakoutMarket = Market(("15 minute", Series("15 minute", 15, breakoutPrices)));
        foreach (ConditionDefinition requestedRule in requestedRules)
        {
            BoundRuleExecution execution = Bound(requestedRule).BindData(breakoutMarket);
            Assert(execution.Evaluate(origin.AddMinutes(31 * 15)).IsMatch, "Requested strategy signals on completed breakout candle");
            Assert(!execution.Evaluate(origin.AddMinutes(32 * 15)).IsMatch, "A crossover is not a persistent above condition");
        }

        assertionCount += new CrossoverEventTests().Run();
        Console.WriteLine($"Passed {assertionCount} regression assertions.");
    }
}

