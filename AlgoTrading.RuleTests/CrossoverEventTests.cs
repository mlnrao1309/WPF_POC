using AlgoTrading.Models.Rules;

internal sealed class CrossoverEventTests
{
    private int assertionCount;
    internal int Run()
    {
        VerifyCurrentAndNextCandles();
        VerifyHistoricalContext();
        VerifyGroupsAndDirections();
        VerifyNestedExponentialAverages();
        VerifyRelativeStrengthValues();
        VerifyMixedTimeframes();
        VerifyCountDoesNotReplayEvents();
        VerifyInvalidRequestsAndSubscriberFailure();
        return assertionCount;
    }

    private void Assert(bool condition, string description)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    private void AssertNumber(double expected, double? actual, string description)
    {
        Assert(actual.HasValue && Math.Abs(expected - actual.Value) < 0.00000001, description);
    }

    private void VerifyCurrentAndNextCandles()
    {
        BoundRule rule = CrossoverTestData.CreateRule(CrossoverTestData.CloseCrossesAbove(2));
        RuleCrossoverMonitor monitor = rule.CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, 1, 3, 4);
        RuleMarketData completeData = CrossoverTestData.CreateMarket("15 minute", candles);
        // A future row is present in memory, but must not leak into an event before its close.
        monitor.Process(completeData, CrossoverTestData.Origin.AddMinutes(30));
        Assert(collector.Detected.Count == 1, "One upward crossover must be emitted.");
        CrossoverOccurrence occurrence = collector.Detected[0];
        Assert(occurrence.Next == null, "A future next candle must remain unavailable.");
        Assert(occurrence.Previous.Candle == candles[0] && occurrence.Current.Candle == candles[1], "Previous and current candles must be included.");
        Assert(occurrence.Direction == ComparisonOperator.CrossedAbove, "The event carries crossover direction.");
        AssertNumber(1, occurrence.Previous.Left.Value, "Previous source value.");
        AssertNumber(3, occurrence.Current.Left.Value, "Current source value.");
        AssertNumber(2, occurrence.Current.Right.Value, "Fixed threshold value.");
        Assert(occurrence.OverallRuleEvaluation.IsMatch, "Overall rule match is reported.");
        monitor.Process(completeData, CrossoverTestData.Origin.AddMinutes(44));
        Assert(collector.Detected.Count == 1 && collector.Completed.Count == 0, "Repeated polling must not repeat a crossover or reveal an unfinished candle.");
        monitor.Process(completeData, CrossoverTestData.Origin.AddMinutes(45));
        Assert(collector.Detected.Count == 1 && collector.Completed.Count == 1, "Next-candle completion updates context without a second crossover.");
        Assert(collector.Completed[0].EventId == occurrence.EventId, "Context update retains the event identity.");
        Assert(HasCandle(collector.Completed[0].Next, candles[2]), "Context update carries the next completed candle.");
        Assert(occurrence.Next == null, "Previously delivered event objects remain immutable.");
        monitor.Process(completeData, CrossoverTestData.Origin.AddMinutes(45));
        Assert(collector.Completed.Count == 1, "Next-candle context is emitted once.");
        RuleCrossoverMonitor growingMonitor = rule.CreateCrossoverMonitor();
        global::CrossoverTestCollector growingCollector = new CrossoverTestCollector();
        growingCollector.Attach(growingMonitor);
        growingMonitor.Process(CrossoverTestData.CreateMarket("15 minute", candles.Take(2).ToArray()), CrossoverTestData.Origin.AddMinutes(30));
        growingMonitor.Process(completeData, CrossoverTestData.Origin.AddMinutes(45));
        Assert(growingCollector.Detected.Count == 1 && growingCollector.Completed.Count == 1, "Deduplication survives replacement market snapshots.");
        Assert(HasCandle(growingCollector.Completed[0].Next, candles[2]), "A newly received candle enriches the pending event.");
    }

    private void VerifyHistoricalContext()
    {
        BoundRule rule = CrossoverTestData.CreateRule(CrossoverTestData.CloseCrossesAbove(2));
        RuleCrossoverMonitor monitor = rule.CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, 1, 3, 1, 4, 5);
        RuleMarketData data = CrossoverTestData.CreateMarket("15 minute", candles);
        monitor.Process(data, CrossoverTestData.Origin.AddMinutes(75));
        Assert(collector.Detected.Count == 2, "A batch must detect all completed historical crossovers, including skipped polling intervals.");
        Assert(HasCandle(collector.Detected[0].Next, candles[2]) && HasCandle(collector.Detected[1].Next, candles[4]), "Known next candles are included during historical replay.");
        Assert(collector.Detected[0].OccurredAt == candles[1].ClosedAt, "Occurrence time is the actual crossover candle close.");
        Assert(collector.Detected[0].ObservedAt == candles[4].ClosedAt, "Observation time is separate from occurrence time.");
        RuleCrossoverMonitor recentMonitor = rule.CreateCrossoverMonitor(CrossoverTestData.Origin.AddMinutes(45));
        global::CrossoverTestCollector recentCollector = new CrossoverTestCollector();
        recentCollector.Attach(recentMonitor);
        recentMonitor.Process(data, CrossoverTestData.Origin.AddMinutes(75));
        Assert(recentCollector.Detected.Count == 1 && recentCollector.Detected[0].OccurredAt == candles[3].ClosedAt, "Start-after excludes old signals without discarding indicator history.");
    }

    private void VerifyGroupsAndDirections()
    {
        ComparisonCondition above = CrossoverTestData.CloseCrossesAbove(2);
        ComparisonCondition below = new ComparisonCondition(above.Left, ComparisonOperator.CrossedBelow, above.Right);
        ComparisonCondition impossible = new ComparisonCondition(new NumberValue(1), ComparisonOperator.GreaterThan, new NumberValue(2));
        ConditionGroup root = new ConditionGroup(GroupOperator.All, TestArray.Create<ConditionDefinition>(impossible, above, below));
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(root).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        monitor.Process(CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, 1, 3, 1)), CrossoverTestData.Origin.AddMinutes(45));
        Assert(collector.Detected.Count == 2, "Crossovers must not be hidden by a false ordinary filter.");
        Assert(collector.Detected[0].Direction == ComparisonOperator.CrossedAbove && collector.Detected[1].Direction == ComparisonOperator.CrossedBelow, "Both directions emit in chronological order.");
        Assert(!collector.Detected[0].OverallRuleEvaluation.IsMatch && !collector.Detected[1].OverallRuleEvaluation.IsMatch, "Crossover occurrence does not imply a complete strategy match.");
        RuleCrossoverMonitor disabledMonitor = CrossoverTestData.CreateRule(new ConditionGroup(GroupOperator.Any, TestArray.Create<ConditionDefinition>(impossible, RuleModelCompatibilityData.DisabledComparison(above)))).CreateCrossoverMonitor();
        global::CrossoverTestCollector disabledCollector = new CrossoverTestCollector();
        disabledCollector.Attach(disabledMonitor);
        disabledMonitor.Process(CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, 1, 3)), CrossoverTestData.Origin.AddMinutes(30));
        Assert(disabledCollector.Detected.Count == 0, "Disabled crossovers emit no events.");
    }

    private void VerifyNestedExponentialAverages()
    {
        IndicatorValue highAverage = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 10, new CandleValue("15 minute", CandleField.High));
        IndicatorValue closeAverage = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 10, new CandleValue("15 minute"));
        IndicatorValue nestedAverage = new IndicatorValue(IndicatorFunction.ExponentialMovingAverage, "15 minute", 10, closeAverage);
        ComparisonCondition comparison = new ComparisonCondition(highAverage, ComparisonOperator.CrossedAbove, nestedAverage);
        decimal[] prices = new decimal[61];
        Array.Fill(prices, 100m, 0, 20);
        Array.Fill(prices, 10m, 20, 20);
        Array.Fill(prices, 200m, 40, 21);
        CompletedCandle[] candles = CrossoverTestData.CreateCandles("15 minute", 15, prices);
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(comparison).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        monitor.Process(CrossoverTestData.CreateMarket("15 minute", candles), candles[candles.Length - 1].ClosedAt);
        Assert(collector.Detected.Count == 1, "The requested nested exponential-average example produces an event.");
        CrossoverOccurrence occurrence = collector.Detected[0];
        Assert(occurrence.Current.Left.IndicatorFunction == IndicatorFunction.ExponentialMovingAverage && occurrence.Current.Left.Length == 10, "Indicator metadata accompanies values.");
        Assert(occurrence.Current.Left.Inputs[0].CandleField == CandleField.High, "High input is identified.");
        Assert(occurrence.Current.Right.Inputs[0].IndicatorFunction == IndicatorFunction.ExponentialMovingAverage, "Inner exponential average is retained.");
        Assert(occurrence.Current.Right.Inputs[0].Inputs[0].CandleField == CandleField.Close, "Nested close source is retained.");
        Assert(occurrence.Previous.Left.Value <= occurrence.Previous.Right.Value && occurrence.Current.Left.Value > occurrence.Current.Right.Value, "The event carries the exact crossing values.");
        Assert(occurrence.Next != null, "An already completed next candle is attached in replay.");
    }

    private void VerifyRelativeStrengthValues()
    {
        IndicatorValue indicator = new IndicatorValue(IndicatorFunction.RelativeStrengthIndex, "15 minute", 2);
        ComparisonCondition condition = new ComparisonCondition(indicator, ComparisonOperator.CrossedAbove, new NumberValue(80));
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(condition).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        monitor.Process(CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, 10, 12, 11, 13)), CrossoverTestData.Origin.AddMinutes(60));
        Assert(collector.Detected.Count == 1, "Relative Strength Index crossover emits an event.");
        CrossoverOccurrence occurrence = collector.Detected[0];
        AssertNumber(100.0 * 2 / 3, occurrence.Previous.Left.Value, "Previous Relative Strength Index value.");
        AssertNumber(100.0 * 6 / 7, occurrence.Current.Left.Value, "Current Relative Strength Index value.");
        AssertNumber(13, occurrence.Current.Left.Inputs[0].Value, "Underlying close is available separately from Relative Strength Index.");
        Assert(occurrence.Current.Left.IndicatorFunction == IndicatorFunction.RelativeStrengthIndex, "Relative Strength Index metadata is explicit.");
        Assert(occurrence.Current.Left.Multiplier == null, "Irrelevant indicator parameters are not reported.");
    }

    private void VerifyMixedTimeframes()
    {
        IndicatorValue fast = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "15 minute", 1);
        IndicatorValue slow = new IndicatorValue(IndicatorFunction.SimpleMovingAverage, "2 hour", 1);
        ComparisonCondition condition = new ComparisonCondition(fast, ComparisonOperator.CrossedAbove, slow);
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(condition).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        decimal[] prices = TestArray.Create<decimal>(9, 9, 9, 9, 9, 9, 9, 9, 11, 12);
        Dictionary<string, IReadOnlyList<CompletedCandle>> series = new Dictionary<string, IReadOnlyList<CompletedCandle>>();
        series.Add("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, prices));
        series.Add("2 hour", CrossoverTestData.CreateCandles("2 hour", 120, 10, 1000));
        monitor.Process(new RuleMarketData(1, series), CrossoverTestData.Origin.AddMinutes(135));
        Assert(collector.Detected.Count == 1, "Mixed timeframe crossover emits once.");
        CrossoverOccurrence occurrence = collector.Detected[0];
        Assert(occurrence.Current.Candle.ClosedAt == CrossoverTestData.Origin.AddMinutes(135), "Event candle uses evaluation clock.");
        Assert((occurrence.Current.Right.SourceCandle != null && occurrence.Current.Right.SourceCandle.ClosedAt == CrossoverTestData.Origin.AddMinutes(120)), "Operand reports actual aligned slower candle.");
        AssertNumber(10, occurrence.Current.Right.Value, "Future slower candle cannot contaminate the value.");
        Assert(occurrence.Next == null, "Future evaluation-clock candle cannot leak into next context.");
    }

    private void VerifyCountDoesNotReplayEvents()
    {
        CountValue count = new CountValue(CrossoverTestData.CloseCrossesAbove(2), "15 minute", 2);
        ComparisonCondition condition = new ComparisonCondition(count, ComparisonOperator.GreaterThanOrEqual, new NumberValue(1));
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(condition).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        RuleMarketData data = CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, 1, 3, 4, 5));
        monitor.Process(data, CrossoverTestData.Origin.AddMinutes(45));
        monitor.Process(data, CrossoverTestData.Origin.AddMinutes(60));
        Assert(collector.Detected.Count == 1, "Historical Count evaluation must not replay the same physical crossover.");
        Assert(collector.Detected[0].OccurredAt == CrossoverTestData.Origin.AddMinutes(30), "Count-contained crossover reports its physical occurrence time.");
    }

    private void VerifyInvalidRequestsAndSubscriberFailure()
    {
        RuleCrossoverMonitor monitor = CrossoverTestData.CreateRule(CrossoverTestData.CloseCrossesAbove(2)).CreateCrossoverMonitor();
        global::CrossoverTestCollector collector = new CrossoverTestCollector();
        monitor.CrossoverDetected += ThrowFromSubscriber;
        collector.Attach(monitor);
        RuleMarketData data = CrossoverTestData.CreateMarket("15 minute", CrossoverTestData.CreateCandles("15 minute", 15, 1, 3));
        bool subscriberFailureReported = false;
        try
        {
            monitor.Process(data, CrossoverTestData.Origin.AddMinutes(30));
        }
        catch (AggregateException)
        {
            subscriberFailureReported = true;
        }

        Assert(subscriberFailureReported && collector.Detected.Count == 1, "One failing subscriber must not prevent delivery to other subscribers.");
        monitor.Process(data, CrossoverTestData.Origin.AddMinutes(30));
        Assert(collector.Detected.Count == 1, "Subscriber failure must not replay committed events.");
        bool backwardsRejected = false;
        try
        {
            monitor.Process(data, CrossoverTestData.Origin.AddMinutes(15));
        }
        catch (ArgumentException)
        {
            backwardsRejected = true;
        }

        Assert(backwardsRejected, "One monitor cannot replay backwards accidentally.");
        Dictionary<string, IReadOnlyList<CompletedCandle>> secondSeries = new Dictionary<string, IReadOnlyList<CompletedCandle>>();
        secondSeries.Add("15 minute", Array.Empty<CompletedCandle>());
        RuleMarketData secondInstrument = new RuleMarketData(2, secondSeries);
        bool instrumentRejected = false;
        try
        {
            monitor.Process(secondInstrument, CrossoverTestData.Origin.AddMinutes(45));
        }
        catch (ArgumentException)
        {
            instrumentRejected = true;
        }

        Assert(instrumentRejected, "Instrument state must not be mixed.");
    }

    private static bool HasCandle(CrossoverCandleSnapshot? snapshot, CompletedCandle candle)
    {
        if (snapshot == null)
        {
            return false;
        }
        return snapshot.Candle == candle;
    }

    private void ThrowFromSubscriber(object? sender, CrossoverEventArgs arguments)
    {
        throw new InvalidOperationException("Intentional subscriber failure for regression coverage.");
    }
}


