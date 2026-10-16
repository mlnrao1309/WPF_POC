using AlgoTrading.Models.Rules;
using System.IO;
using System.Text.Json;

internal sealed class DatabaseCrossoverTest
{
    internal async Task RunAsync(string outputDirectory)
    {
        DatabaseCrossoverSettings settings = DatabaseCrossoverSettings.Load();
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        IReadOnlyList<DatabaseCandleRow> rows = await new DatabaseCandleReader().ReadAsync(settings, asOf);
        if (rows.Count < 11) throw new InvalidOperationException("At least eleven completed database candles are required for this test.");
        var completedCandles = new List<CompletedCandle>();
        foreach (DatabaseCandleRow row in rows) completedCandles.Add(row.CompletedCandle);
        var series = new Dictionary<string, IReadOnlyList<CompletedCandle>> { { settings.Timeframe, completedCandles.AsReadOnly() } };
        var marketData = new RuleMarketData(settings.InstrumentToken, series);
        RuleDefinition definition = new DatabaseCrossoverRuleFactory().Create(settings);
        BoundRule rule = RuleBinder.Bind(definition);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "rule.json"), RuleDefinitionJson.Serialize(definition));
        using var logger = new DatabaseCrossoverLogger(outputDirectory, rows, definition, marketData,
            TimeZoneInfo.FindSystemTimeZoneById(settings.TimestampTimeZoneId));
        RuleCrossoverMonitor monitor = rule.CreateCrossoverMonitor();
        monitor.CrossoverDetected += logger.OnCrossoverDetected;
        monitor.Process(marketData, asOf);
        int firstPassCount = logger.CrossoverCount;
        monitor.Process(marketData, asOf);
        if (logger.CrossoverCount != firstPassCount) throw new InvalidOperationException("Repeated replay duplicated crossover events.");
        VerifyCrossovers(rows, logger.LoggedTimes);
        string summary = $"Instrument: {settings.InstrumentToken}\nTimeframe: {settings.Timeframe}\nCompleted candles: {rows.Count}\nFirst candle: {rows[0].Timestamp:O}\nLast candle: {rows[^1].Timestamp:O}\nTimestamp timezone: {settings.TimestampTimeZoneId}\nTimestamp represents candle open: {settings.TimestampRepresentsCandleOpen}\nObservation timestamp: {asOf:O}\nCrossed-above events: {logger.CrossoverCount}\nAll four conditions matched: {logger.StrategyMatchCount}\nValidation: direct exponential-average recurrence agrees with every crossover; full strategy matches agree with all four conditions; repeat processing produced no duplicates.\n";
        File.WriteAllText(Path.Combine(outputDirectory, "summary.txt"), summary);
        Console.WriteLine(summary);
        Console.WriteLine("Logs: " + Path.GetFullPath(outputDirectory));
    }

    private static void VerifyCrossovers(IReadOnlyList<DatabaseCandleRow> rows, IReadOnlySet<DateTimeOffset> loggedTimes)
    {
        const double smoothingFactor = 2.0 / 6.0;
        double exponentialAverage = (double)rows[0].Close;
        double nestedAverage = exponentialAverage;
        int expectedCount = 0;
        for (int candleIndex = 1; candleIndex < rows.Count; candleIndex++)
        {
            double previousAverage = exponentialAverage;
            double previousNestedAverage = nestedAverage;
            exponentialAverage += smoothingFactor * ((double)rows[candleIndex].Close - exponentialAverage);
            nestedAverage += smoothingFactor * (exponentialAverage - nestedAverage);
            bool expected = previousAverage <= previousNestedAverage && exponentialAverage > nestedAverage;
            if (expected) expectedCount++;
            if (loggedTimes.Contains(rows[candleIndex].CompletedCandle.ClosedAt) != expected)
                throw new InvalidOperationException($"Database crossover mismatch at {rows[candleIndex].Timestamp:O}.");
        }
        if (expectedCount != loggedTimes.Count) throw new InvalidOperationException("Database crossover counts do not agree.");
    }
}

