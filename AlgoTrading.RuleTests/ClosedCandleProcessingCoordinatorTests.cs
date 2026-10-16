using AlgoTrading.Models;
using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Ingestion;
using AlgoTrading.Models.MarketData.Processing;
using AlgoTrading.Models.Rules;

internal sealed class ClosedCandleProcessingCoordinatorTests
{
    private int assertions;

    public void Run()
    {
        this.VerifyNormalSessionAggregationAndReplay();
        this.VerifyMissingSourceAndFutureRejection();
        this.VerifyClosedAndSpecialSessions();
        this.VerifyDailyCalendarAggregation();
        this.VerifyMalformedAndWrongInputs();
        Console.WriteLine("Passed " + this.assertions + " closed-candle coordinator assertions.");
    }

    private void VerifyNormalSessionAggregationAndReplay()
    {
        int instrumentToken = 265;
        DateOnly date = new DateOnly(2026, 9, 28);
        TradingSession session = CreateSession(instrumentToken, date, new TimeOnly(9, 15), new TimeOnly(15, 30));
        TradingSessionCalendar calendar = CreateSingleDayCalendar(instrumentToken, date, session, "normal-r1");
        PlatformConfigurationSnapshot configuration = CreateIntradayConfiguration();
        ClosedCandleProcessingCoordinator coordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, configuration);
        List<CompletedTargetCandle> emitted = new List<CompletedTargetCandle>();
        FinalizedSourceCandleEvent? lastEvent = null;
        DateTimeOffset openedAt = session.OpenedAt;
        decimal price = 100m;
        while (openedAt < session.ClosedAt)
        {
            DateTimeOffset closedAt = openedAt.AddMinutes(5);
            if (closedAt > session.ClosedAt)
            {
                closedAt = session.ClosedAt;
            }

            lastEvent = CreateEvent(instrumentToken, HistoricalStreamTypes.IntradayFiveMinute, "5minute",
                openedAt, closedAt, price, true, closedAt);
            ClosedCandleProcessingResult result = coordinator.Process(lastEvent);
            this.Assert(result.Status == ClosedCandleProcessingStatus.Accepted,
                "Every contiguous finalized 5-minute candle is accepted.");
            emitted.AddRange(result.CompletedCandles);
            openedAt = closedAt;
            price += 1m;
        }

        this.Assert(Count(emitted, "5m") == 75, "A normal session emits 75 five-minute targets.");
        this.Assert(Count(emitted, "15m") == 25, "A normal session emits 25 fifteen-minute targets.");
        this.Assert(Count(emitted, "25m") == 15, "A normal session emits 15 twenty-five-minute targets.");
        this.Assert(Count(emitted, "60m") == 7, "A normal session emits six full and one shortened hourly target.");
        this.Assert(Count(emitted, "75m") == 5, "A normal session emits five seventy-five-minute targets.");

        CompletedTargetCandle? finalHourly = FindLast(emitted, "60m");
        this.Assert(finalHourly != null && finalHourly.Candle.ClosedAt == session.ClosedAt
            && finalHourly.Candle.Candle.OpenedAt == session.ClosedAt.AddMinutes(-15),
            "The final shortened hourly bar is force-sealed at session close.");
        this.Assert(finalHourly != null && finalHourly.CalendarRevision == "normal-r1",
            "Every output carries the selected calendar revision.");

        if (lastEvent == null)
        {
            throw new InvalidOperationException("Synthetic session produced no event.");
        }

        ClosedCandleProcessingResult replay = coordinator.Process(lastEvent);
        this.Assert(replay.Status == ClosedCandleProcessingStatus.Duplicate
            && replay.CompletedCandles.Count == 0,
            "Replaying a finalized source candle emits no duplicate target candle.");
    }

    private void VerifyMissingSourceAndFutureRejection()
    {
        int instrumentToken = 265;
        DateOnly date = new DateOnly(2026, 9, 28);
        TradingSession session = CreateSession(instrumentToken, date, new TimeOnly(9, 15), new TimeOnly(15, 30));
        TradingSessionCalendar calendar = CreateSingleDayCalendar(instrumentToken, date, session, "gap-r1");
        ClosedCandleProcessingCoordinator coordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, CreateIntradayConfiguration());
        FinalizedSourceCandleEvent first = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", session.OpenedAt, session.OpenedAt.AddMinutes(5),
            100m, true, session.OpenedAt.AddMinutes(5));
        coordinator.Process(first);
        FinalizedSourceCandleEvent afterGap = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", session.OpenedAt.AddMinutes(10),
            session.OpenedAt.AddMinutes(15), 102m, true, session.OpenedAt.AddMinutes(15));

        ClosedCandleProcessingResult gap = coordinator.Process(afterGap);
        this.Assert(gap.Status == ClosedCandleProcessingStatus.IncompleteCoverage
            && gap.CompletedCandles.Count == 0,
            "A missing source candle invalidates affected target periods instead of fabricating output.");

        DateOnly nextDate = date.AddDays(1);
        TradingSession nextSession = CreateSession(instrumentToken, nextDate, new TimeOnly(9, 15),
            new TimeOnly(15, 30));
        List<TradingCalendarDay> twoDays = new List<TradingCalendarDay>();
        twoDays.Add(new TradingCalendarDay(date, session));
        twoDays.Add(new TradingCalendarDay(nextDate, nextSession));
        TradingSessionCalendar twoDayCalendar = new TradingSessionCalendar(instrumentToken, date, nextDate,
            "gap-reset-r1", twoDays);
        ClosedCandleProcessingCoordinator resetCoordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            twoDayCalendar, CreateIntradayConfiguration());
        resetCoordinator.Process(first);
        resetCoordinator.Process(afterGap);
        FinalizedSourceCandleEvent nextSessionFirst = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", nextSession.OpenedAt,
            nextSession.OpenedAt.AddMinutes(5), 110m, true, nextSession.OpenedAt.AddMinutes(5));
        this.Assert(resetCoordinator.Process(nextSessionFirst).Status == ClosedCandleProcessingStatus.Accepted,
            "An invalid session cannot bleed state into the next explicit session.");

        ClosedCandleProcessingCoordinator futureCoordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, CreateIntradayConfiguration());
        FinalizedSourceCandleEvent early = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", session.OpenedAt, session.OpenedAt.AddMinutes(5),
            100m, true, session.OpenedAt.AddMinutes(4));
        ClosedCandleProcessingResult future = futureCoordinator.Process(early);
        this.Assert(future.Status == ClosedCandleProcessingStatus.RejectedNotFinalized,
            "A future completion cannot become observable before its close.");

        FinalizedSourceCandleEvent notFinalized = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", session.OpenedAt, session.OpenedAt.AddMinutes(5),
            100m, false, session.OpenedAt.AddMinutes(5));
        ClosedCandleProcessingResult providerPending = futureCoordinator.Process(notFinalized);
        this.Assert(providerPending.Status == ClosedCandleProcessingStatus.RejectedNotFinalized,
            "Wall-clock completion without provider finalization is rejected.");
    }

    private void VerifyClosedAndSpecialSessions()
    {
        int instrumentToken = 265;
        DateOnly saturday = new DateOnly(2026, 9, 26);
        DateOnly sunday = new DateOnly(2026, 9, 27);
        TradingSession special = CreateSession(instrumentToken, sunday, new TimeOnly(18, 0), new TimeOnly(18, 13));
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        days.Add(new TradingCalendarDay(saturday, null));
        days.Add(new TradingCalendarDay(sunday, special));
        TradingSessionCalendar calendar = new TradingSessionCalendar(instrumentToken, saturday, sunday,
            "special-r2", days);
        ClosedCandleProcessingCoordinator coordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, CreateIntradayConfiguration());

        DateTimeOffset closedDayOpen = MarketTimestamp.FromDatabase(saturday.ToDateTime(new TimeOnly(9, 15)));
        FinalizedSourceCandleEvent closedDay = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", closedDayOpen, closedDayOpen.AddMinutes(5),
            100m, true, closedDayOpen.AddMinutes(5));
        this.Assert(coordinator.Process(closedDay).Status == ClosedCandleProcessingStatus.ClosedSession,
            "A weekend or holiday explicitly closed by the calendar is rejected.");

        List<CompletedTargetCandle> emitted = new List<CompletedTargetCandle>();
        DateTimeOffset start = special.OpenedAt;
        while (start < special.ClosedAt)
        {
            DateTimeOffset close = start.AddMinutes(5);
            if (close > special.ClosedAt)
            {
                close = special.ClosedAt;
            }

            FinalizedSourceCandleEvent source = CreateEvent(instrumentToken,
                HistoricalStreamTypes.IntradayFiveMinute, "5minute", start, close, 100m, true, close);
            ClosedCandleProcessingResult result = coordinator.Process(source);
            this.Assert(result.Status == ClosedCandleProcessingStatus.Accepted,
                "An explicit special-session source candle is accepted.");
            emitted.AddRange(result.CompletedCandles);
            start = close;
        }

        this.Assert(Count(emitted, "15m") == 1 && Count(emitted, "75m") == 1,
            "Targets longer than a special session are force-sealed once at its explicit close.");
        CompletedTargetCandle? specialTarget = FindLast(emitted, "75m");
        this.Assert(specialTarget != null && specialTarget.Candle.ClosedAt == special.ClosedAt,
            "The shortened special-session target uses the official close.");
    }

    private void VerifyDailyCalendarAggregation()
    {
        int instrumentToken = 265;
        DateOnly firstDate = new DateOnly(2026, 8, 31);
        DateOnly lastDate = new DateOnly(2026, 10, 4);
        TradingSessionCalendar calendar = CreateWeekdayCalendar(instrumentToken, firstDate, lastDate, "daily-r3");
        PlatformConfigurationSnapshot configuration = CreateDailyConfiguration();
        ClosedCandleProcessingCoordinator coordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, configuration);
        List<CompletedTargetCandle> emitted = new List<CompletedTargetCandle>();
        DateOnly date = firstDate;
        while (date <= new DateOnly(2026, 9, 30))
        {
            CalendarLookupResult lookup = calendar.GetSession(instrumentToken, date);
            if (lookup.Status == CalendarLookupStatus.Found && lookup.Session != null)
            {
                FinalizedSourceCandleEvent source = CreateEvent(instrumentToken,
                    HistoricalStreamTypes.DailyOneDay, "D", lookup.Session.OpenedAt, lookup.Session.ClosedAt,
                    date.Day, true, lookup.Session.ClosedAt);
                ClosedCandleProcessingResult result = coordinator.Process(source);
                this.Assert(result.Status == ClosedCandleProcessingStatus.Accepted,
                    "A complete daily source session is accepted.");
                emitted.AddRange(result.CompletedCandles);
            }

            date = date.AddDays(1);
        }

        this.Assert(Count(emitted, "1D") == 22, "Each September trading session emits one daily target.");
        this.Assert(Count(emitted, "1W") == 4, "Only four fully closed September weeks emit in the supplied prefix.");
        this.Assert(Count(emitted, "1M") == 1, "The completed September calendar month emits once.");

        ClosedCandleProcessingCoordinator missingCoordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, configuration);
        DateOnly monday = new DateOnly(2026, 9, 7);
        ClosedCandleProcessingResult finalResult = ProcessWeekWithMissingTuesday(missingCoordinator, calendar,
            instrumentToken, monday);
        this.Assert(finalResult.Status == ClosedCandleProcessingStatus.IncompleteCoverage,
            "A missing daily source makes the affected weekly target unavailable.");
    }

    private void VerifyMalformedAndWrongInputs()
    {
        int instrumentToken = 265;
        DateOnly date = new DateOnly(2026, 9, 28);
        TradingSession session = CreateSession(instrumentToken, date, new TimeOnly(9, 15), new TimeOnly(15, 30));
        TradingSessionCalendar calendar = CreateSingleDayCalendar(instrumentToken, date, session, "reject-r4");
        ClosedCandleProcessingCoordinator coordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            calendar, CreateIntradayConfiguration());

        FinalizedSourceCandleEvent wrongInstrument = CreateEvent(999,
            HistoricalStreamTypes.IntradayFiveMinute, "5minute", session.OpenedAt, session.OpenedAt.AddMinutes(5),
            100m, true, session.OpenedAt.AddMinutes(5));
        this.Assert(coordinator.Process(wrongInstrument).Status == ClosedCandleProcessingStatus.RejectedInstrument,
            "A wrong-instrument source candle is rejected.");

        FinalizedSourceCandleEvent wrongTimeframe = CreateEvent(instrumentToken,
            HistoricalStreamTypes.IntradayFiveMinute, "15minute", session.OpenedAt, session.OpenedAt.AddMinutes(5),
            100m, true, session.OpenedAt.AddMinutes(5));
        this.Assert(coordinator.Process(wrongTimeframe).Status == ClosedCandleProcessingStatus.RejectedTimeframe,
            "A source candle with the wrong timeframe is rejected.");

        Candle malformed = new Candle(instrumentToken, "5minute", MarketTimestamp.ToDatabase(session.OpenedAt),
            100m, 90m, 95m, 100m, 10);
        CompletedCandle malformedCompleted = new CompletedCandle(malformed, session.OpenedAt.AddMinutes(5));
        FinalizedSourceCandleEvent malformedEvent = new FinalizedSourceCandleEvent(
            HistoricalStreamTypes.IntradayFiveMinute, malformedCompleted, session.OpenedAt.AddMinutes(5), true);
        this.Assert(coordinator.Process(malformedEvent).Status == ClosedCandleProcessingStatus.RejectedMalformed,
            "Malformed OHLC is rejected before aggregation.");

        DateOnly nextDate = date.AddDays(1);
        TradingSession nextSession = CreateSession(instrumentToken, nextDate, new TimeOnly(9, 15),
            new TimeOnly(15, 30));
        List<TradingCalendarDay> orderedDays = new List<TradingCalendarDay>();
        orderedDays.Add(new TradingCalendarDay(date, session));
        orderedDays.Add(new TradingCalendarDay(nextDate, nextSession));
        TradingSessionCalendar orderedCalendar = new TradingSessionCalendar(instrumentToken, date, nextDate,
            "order-r5", orderedDays);
        ClosedCandleProcessingCoordinator dailyCoordinator = new ClosedCandleProcessingCoordinator(instrumentToken,
            orderedCalendar, CreateDailyOnlyConfiguration());
        FinalizedSourceCandleEvent newer = CreateEvent(instrumentToken, HistoricalStreamTypes.DailyOneDay, "D",
            nextSession.OpenedAt, nextSession.ClosedAt, 101m, true, nextSession.ClosedAt);
        dailyCoordinator.Process(newer);
        FinalizedSourceCandleEvent older = CreateEvent(instrumentToken, HistoricalStreamTypes.DailyOneDay, "D",
            session.OpenedAt, session.ClosedAt, 100m, true, session.ClosedAt);
        this.Assert(dailyCoordinator.Process(older).Status == ClosedCandleProcessingStatus.RejectedOutOfOrder,
            "An out-of-order source candle is rejected without rewinding state.");
    }

    private static ClosedCandleProcessingResult ProcessWeekWithMissingTuesday(
        ClosedCandleProcessingCoordinator coordinator, ITradingSessionCalendar calendar, int instrumentToken,
        DateOnly monday)
    {
        ClosedCandleProcessingResult? last = null;
        for (int offset = 0; offset < 5; offset++)
        {
            if (offset == 1)
            {
                continue;
            }

            DateOnly date = monday.AddDays(offset);
            CalendarLookupResult lookup = calendar.GetSession(instrumentToken, date);
            if (lookup.Session == null)
            {
                throw new InvalidOperationException("Synthetic weekday calendar is incomplete.");
            }

            FinalizedSourceCandleEvent source = CreateEvent(instrumentToken, HistoricalStreamTypes.DailyOneDay,
                "D", lookup.Session.OpenedAt, lookup.Session.ClosedAt, 100m + offset, true,
                lookup.Session.ClosedAt);
            last = coordinator.Process(source);
        }

        if (last == null)
        {
            throw new InvalidOperationException("Synthetic week produced no result.");
        }

        return last;
    }

    private static PlatformConfigurationSnapshot CreateIntradayConfiguration()
    {
        List<TimeframeOption> options = new List<TimeframeOption>();
        options.Add(new TimeframeOption("5m", "5minute", 5, TimeframeSourceStream.IntradayFiveMinute, true));
        options.Add(new TimeframeOption("15m", "15minute", 15, TimeframeSourceStream.IntradayFiveMinute, true));
        options.Add(new TimeframeOption("25m", "25minute", 25, TimeframeSourceStream.IntradayFiveMinute, true));
        options.Add(new TimeframeOption("60m", "60minute", 60, TimeframeSourceStream.IntradayFiveMinute, true));
        options.Add(new TimeframeOption("75m", "75minute", 75, TimeframeSourceStream.IntradayFiveMinute, true));
        IReadOnlyList<ActiveCriticalLevelRule> noRules = new List<ActiveCriticalLevelRule>().AsReadOnly();
        return new PlatformConfigurationSnapshot(new TimeframeCatalog(options), noRules, "intraday-config-r1");
    }

    private static PlatformConfigurationSnapshot CreateDailyConfiguration()
    {
        List<TimeframeOption> options = new List<TimeframeOption>();
        options.Add(new TimeframeOption("1D", "D", 0, TimeframeSourceStream.DailyOneDay, true));
        options.Add(new TimeframeOption("1W", "W", 0, TimeframeSourceStream.DailyOneDay, true));
        options.Add(new TimeframeOption("1M", "M", 0, TimeframeSourceStream.DailyOneDay, true));
        IReadOnlyList<ActiveCriticalLevelRule> noRules = new List<ActiveCriticalLevelRule>().AsReadOnly();
        return new PlatformConfigurationSnapshot(new TimeframeCatalog(options), noRules, "daily-config-r1");
    }

    private static PlatformConfigurationSnapshot CreateDailyOnlyConfiguration()
    {
        List<TimeframeOption> options = new List<TimeframeOption>();
        options.Add(new TimeframeOption("1D", "D", 0, TimeframeSourceStream.DailyOneDay, true));
        IReadOnlyList<ActiveCriticalLevelRule> noRules = new List<ActiveCriticalLevelRule>().AsReadOnly();
        return new PlatformConfigurationSnapshot(new TimeframeCatalog(options), noRules, "daily-only-config-r1");
    }

    private static TradingSessionCalendar CreateSingleDayCalendar(int instrumentToken, DateOnly date,
        TradingSession session, string revision)
    {
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        days.Add(new TradingCalendarDay(date, session));
        return new TradingSessionCalendar(instrumentToken, date, date, revision, days);
    }

    private static TradingSessionCalendar CreateWeekdayCalendar(int instrumentToken, DateOnly firstDate,
        DateOnly lastDate, string revision)
    {
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        DateOnly date = firstDate;
        while (date <= lastDate)
        {
            TradingSession? session = null;
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday
                && date != new DateOnly(2026, 8, 31))
            {
                session = CreateSession(instrumentToken, date, new TimeOnly(9, 15), new TimeOnly(15, 30));
            }

            days.Add(new TradingCalendarDay(date, session));
            date = date.AddDays(1);
        }

        return new TradingSessionCalendar(instrumentToken, firstDate, lastDate, revision, days);
    }

    private static TradingSession CreateSession(int instrumentToken, DateOnly date, TimeOnly open, TimeOnly close)
    {
        return new TradingSession(instrumentToken, MarketTimestamp.FromDatabase(date.ToDateTime(open)),
            MarketTimestamp.FromDatabase(date.ToDateTime(close)));
    }

    private static FinalizedSourceCandleEvent CreateEvent(int instrumentToken, string streamType, string timeframe,
        DateTimeOffset openedAt, DateTimeOffset closedAt, decimal price, bool providerFinalized,
        DateTimeOffset observedAt)
    {
        Candle candle = new Candle(instrumentToken, timeframe, MarketTimestamp.ToDatabase(openedAt), price,
            price + 1m, price - 1m, price + 0.5m, 100);
        CompletedCandle completed = new CompletedCandle(candle, closedAt);
        return new FinalizedSourceCandleEvent(streamType, completed, observedAt, providerFinalized);
    }

    private static int Count(IReadOnlyList<CompletedTargetCandle> candles, string timeframeCode)
    {
        int count = 0;
        for (int index = 0; index < candles.Count; index++)
        {
            if (candles[index].TimeframeCode == timeframeCode)
            {
                count++;
            }
        }

        return count;
    }

    private static CompletedTargetCandle? FindLast(IReadOnlyList<CompletedTargetCandle> candles,
        string timeframeCode)
    {
        for (int index = candles.Count - 1; index >= 0; index--)
        {
            if (candles[index].TimeframeCode == timeframeCode)
            {
                return candles[index];
            }
        }

        return null;
    }

    private void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        this.assertions++;
    }
}
