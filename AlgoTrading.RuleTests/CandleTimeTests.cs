using AlgoTrading.Models;
using AlgoTrading.Models.Rules;
using System.Globalization;

internal sealed class CandleTimeTests
{
    private int assertionCount;

    internal void Run()
    {
        VerifyTimestampConversion();
        VerifySessionBoundaries();
        VerifyInvalidCandles();
        VerifyReplay();
        Console.WriteLine("Passed " + assertionCount + " candle timing assertions.");
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private DateTimeOffset ParseInstant(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }

    private void VerifyTimestampConversion()
    {
        DateTime databaseStart = new DateTime(2026, 9, 24, 9, 15, 0, DateTimeKind.Unspecified);
        DateTime utcStart = new DateTime(2026, 9, 24, 3, 45, 0, DateTimeKind.Utc);
        DateTimeOffset instant = MarketTimestamp.FromDatabase(databaseStart);
        Assert(instant.UtcDateTime == utcStart, "09:15 IST must mean 03:45 UTC.");
        Assert(instant.Offset == TimeSpan.FromMinutes(330), "IST offset must be explicit.");
        Assert(MarketTimestamp.ToDatabase(instant) == databaseStart, "Database timestamp must round trip.");
        Assert(MarketTimestamp.ToDatabase(instant).Kind == DateTimeKind.Unspecified, "SQL uses an unspecified wall time.");
        Candle candle = new Candle(265, "60 minute", databaseStart, 100, 101, 99, 100, 10);
        Candle utcCandle = new Candle(265, "60 minute", utcStart, 100, 101, 99, 100, 10);
        Assert(candle.Timestamp == utcStart && candle.Timestamp.Kind == DateTimeKind.Utc, "Legacy Timestamp must expose the correct UTC instant.");
        Assert(candle.OpenedAt == utcCandle.OpenedAt, "Explicit UTC must not be converted twice.");
        Assert(candle.SourceTimestamp == databaseStart && candle.SourceTimestamp.Kind == DateTimeKind.Unspecified, "Preserve the supplied database timestamp.");
        Assert(utcCandle.SourceTimestamp.Kind == DateTimeKind.Utc, "Preserve explicitly supplied UTC provenance.");
        bool rejected = false;
        try
        {
            MarketTimestamp.FromDatabase(utcStart);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "The strict database adapter must reject already-converted UTC input.");
        rejected = false;
        try
        {
            MarketTimestamp.FromDateTime(DateTime.SpecifyKind(databaseStart, DateTimeKind.Local));
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "Machine-local time must not silently affect candle identity.");
    }

    private void VerifySessionBoundaries()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "candle-time-boundaries.csv");
        string[] rows = File.ReadAllLines(path);
        IntradayCandleClock clock = new IntradayCandleClock();
        for (int index = 1; index < rows.Length; index++)
        {
            string[] cells = rows[index].Split(',');
            DateTimeOffset start = ParseInstant(cells[3]);
            TimeSpan duration = TimeSpan.FromMinutes(int.Parse(cells[4], CultureInfo.InvariantCulture));
            bool rejected = false;
            try
            {
                TradingSession session = new TradingSession(265, ParseInstant(cells[1]), ParseInstant(cells[2]));
                DateTimeOffset completion = clock.GetCompletion(start, duration, session);
                Assert(cells[6] == "valid", cells[0] + ": invalid input was accepted.");
                Assert(completion == ParseInstant(cells[5]), cells[0] + ": incorrect completion.");
                Assert(completion.Offset == TimeSpan.FromMinutes(330), cells[0] + ": completion must retain IST offset.");
                Candle candle = new Candle(265, "test interval", MarketTimestamp.ToDatabase(start), 100, 101, 99, 100, 10);
                CompletedCandle? completed;
                Assert(!clock.TryCreateCompleted(candle, duration, session, completion.AddTicks(-1), true, out completed)
                    && completed == null, cells[0] + ": must not publish before close.");
                Assert(!clock.TryCreateCompleted(candle, duration, session, completion, false, out completed)
                    && completed == null, cells[0] + ": unfinalized input must remain unavailable.");
                Assert(clock.TryCreateCompleted(candle, duration, session, completion, true, out completed)
                    && completed != null && completed.ClosedAt == completion, cells[0] + ": finalized candle must be available at close.");
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert(rejected == (cells[6] == "invalid"), cells[0] + ": incorrect validation result.");
        }
    }

    private RuleMarketData CreateMarket(IReadOnlyList<CompletedCandle> candles)
    {
        Dictionary<string, IReadOnlyList<CompletedCandle>> series = new Dictionary<string, IReadOnlyList<CompletedCandle>>();
        series.Add("60 minute", candles);
        return new RuleMarketData(265, series);
    }

    private void VerifyInvalidCandles()
    {
        DateTimeOffset start = ParseInstant("2026-09-24T09:15:00+05:30");
        TradingSession session = new TradingSession(265, start, start.AddHours(1));
        IntradayCandleClock clock = new IntradayCandleClock();
        Candle[] invalidCandles = new Candle[3];
        invalidCandles[0] = new Candle(999, "60 minute", start.UtcDateTime, 100, 101, 99, 100, 10);
        invalidCandles[1] = new Candle(265, "60 minute", start.UtcDateTime, 100, 99, 101, 100, 10);
        invalidCandles[2] = new Candle(265, "60 minute", start.UtcDateTime, 100, 101, 99, 100, -1);
        foreach (Candle candle in invalidCandles)
        {
            bool rejected = false;
            try
            {
                CompletedCandle? completed;
                clock.TryCreateCompleted(candle, TimeSpan.FromHours(1), session, session.ClosedAt, true, out completed);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert(rejected, "Wrong instrument or malformed data must not become a completed candle.");
        }
    }

    private void VerifyReplay()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "candle-time-replay.csv");
        string[] rows = File.ReadAllLines(path);
        TradingSession session = new TradingSession(265, ParseInstant("2026-09-24T09:15:00+05:30"), ParseInstant("2026-09-24T15:30:00+05:30"));
        IntradayCandleClock clock = new IntradayCandleClock();
        List<CompletedCandle> fullHistory = new List<CompletedCandle>();
        for (int index = 1; index < rows.Length; index++)
        {
            string[] cells = rows[index].Split(',');
            DateTime sourceTime = DateTime.Parse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.None);
            Candle candle = new Candle(265, "60 minute", sourceTime,
                decimal.Parse(cells[1], CultureInfo.InvariantCulture), decimal.Parse(cells[2], CultureInfo.InvariantCulture),
                decimal.Parse(cells[3], CultureInfo.InvariantCulture), decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                long.Parse(cells[5], CultureInfo.InvariantCulture));
            CompletedCandle? completed;
            bool available = clock.TryCreateCompleted(candle, TimeSpan.FromMinutes(60), session, session.ClosedAt, true, out completed);
            if (!available || completed == null)
            {
                throw new InvalidOperationException("The finalized replay fixture was unexpectedly unavailable.");
            }
            fullHistory.Add(completed);
        }

        ComparisonCondition condition = new ComparisonCondition(new CandleValue("60 minute"), ComparisonOperator.CrossedAbove, new NumberValue(100));
        BoundRule rule = RuleBinder.Bind(new RuleDefinition("IST final candle cross", "60 minute", condition));
        RuleMarketData fullMarket = CreateMarket(fullHistory);
        DateTimeOffset[] observations = new DateTimeOffset[4];
        observations[0] = ParseInstant("2026-09-24T15:14:59+05:30");
        observations[1] = ParseInstant("2026-09-24T15:15:00+05:30");
        observations[2] = ParseInstant("2026-09-24T15:29:59+05:30");
        observations[3] = ParseInstant("2026-09-24T15:30:00+05:30");
        foreach (DateTimeOffset observedAt in observations)
        {
            List<CompletedCandle> prefix = new List<CompletedCandle>();
            foreach (CompletedCandle candle in fullHistory)
            {
                if (candle.ClosedAt <= observedAt)
                {
                    prefix.Add(candle);
                }
            }
            RuleStatus fullStatus = rule.BindData(fullMarket).Evaluate(observedAt).Status;
            RuleStatus prefixStatus = rule.BindData(CreateMarket(prefix)).Evaluate(observedAt).Status;
            Assert(fullStatus == prefixStatus, "Full-history and prefix-only evaluations must agree.");
            Assert((fullStatus == RuleStatus.Matched) == (observedAt == session.ClosedAt), "The shortened candle may cross only at 15:30 IST.");
        }

        RuleCrossoverMonitor monitor = rule.CreateCrossoverMonitor();
        CrossoverTestCollector collector = new CrossoverTestCollector();
        collector.Attach(monitor);
        monitor.Process(fullMarket, session.ClosedAt.AddTicks(-1));
        Assert(collector.Detected.Count == 0, "No crossover event may leak from an unfinished final candle.");
        monitor.Process(fullMarket, session.ClosedAt);
        Assert(collector.Detected.Count == 1, "The session-end crossover must raise one event.");
        CrossoverOccurrence occurrence = collector.Detected[0];
        Assert(occurrence.OccurredAt == session.ClosedAt, "Event time must use the shortened candle completion.");
        Assert(occurrence.Previous.Candle.ClosedAt == ParseInstant("2026-09-24T15:15:00+05:30"), "Previous event candle must end at 15:15.");
        Assert(occurrence.Current.Candle.Candle.SourceTimestamp == DateTime.Parse("2026-09-24T15:15:00", CultureInfo.InvariantCulture), "Event must preserve the original IST start.");
        Assert(occurrence.Next == null, "A nonexistent next candle must not be invented.");
        monitor.Process(fullMarket, session.ClosedAt);
        Assert(collector.Detected.Count == 1, "Repeated session-end processing must not duplicate the event.");
    }
}
