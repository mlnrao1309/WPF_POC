using AlgoTrading.Models;
using AlgoTrading.Models.Rules;
using System.Globalization;

internal sealed class TradingCalendarTests
{
    private int assertionCount;

    internal void Run()
    {
        List<TradingCalendarDay> days = ReadDays();
        TradingSessionCalendar calendar = CreateCalendar(days);
        VerifyLookups(calendar);
        VerifyValidation(days);
        VerifyClockIntegration(calendar);
        days.Clear();
        Assert(calendar.GetSession(265, new DateOnly(2026, 9, 24)).Status == CalendarLookupStatus.Found,
            "Calendar must not change when its input list changes.");
        Assert(calendar.Revision == "synthetic-calendar-v1", "Calendar snapshot must retain its supplied revision.");
        Console.WriteLine("Passed " + assertionCount + " calendar assertions.");
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private TradingSessionCalendar CreateCalendar(IReadOnlyList<TradingCalendarDay> days)
    {
        return new TradingSessionCalendar(265, new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 29),
            "synthetic-calendar-v1", days);
    }

    private List<TradingCalendarDay> ReadDays()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendar-days.csv");
        string[] lines = File.ReadAllLines(path);
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            DateOnly date = DateOnly.ParseExact(cells[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            TradingSession? session = null;
            if (cells[1].Length != 0)
            {
                DateTime start = date.ToDateTime(TimeOnly.ParseExact(cells[1], "HH:mm", CultureInfo.InvariantCulture));
                DateTime end = date.ToDateTime(TimeOnly.ParseExact(cells[2], "HH:mm", CultureInfo.InvariantCulture));
                session = new TradingSession(265, MarketTimestamp.FromDatabase(start), MarketTimestamp.FromDatabase(end));
            }
            days.Add(new TradingCalendarDay(date, session));
        }
        return days;
    }

    private void VerifyLookups(ITradingSessionCalendar calendar)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendar-lookups.csv");
        string[] lines = File.ReadAllLines(path);
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            int instrument = int.Parse(cells[1], CultureInfo.InvariantCulture);
            CalendarLookupResult result;
            if (cells[0] == "Date")
            {
                result = calendar.GetSession(instrument, DateOnly.ParseExact(cells[2], "yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                result = calendar.GetNextSession(instrument, DateTimeOffset.Parse(cells[2], CultureInfo.InvariantCulture));
            }
            CalendarLookupStatus expected = Enum.Parse<CalendarLookupStatus>(cells[3]);
            Assert(result.Status == expected, "Calendar lookup status mismatch on fixture row " + index);
            if (expected == CalendarLookupStatus.Found)
            {
                TradingSession? session = result.Session;
                Assert(session != null, "Found lookup must return its session.");
                if (session == null)
                {
                    throw new InvalidOperationException("Session missing.");
                }
                Assert(session.OpenedAt == DateTimeOffset.Parse(cells[4], CultureInfo.InvariantCulture),
                    "Wrong session opening on fixture row " + index);
                Assert(session.InstrumentToken == instrument, "Lookup must preserve instrument identity.");
            }
            else
            {
                Assert(result.Session == null, "Unavailable/closed dates cannot return an invented session.");
            }
        }
    }

    private void VerifyRejectedCalendar(List<TradingCalendarDay> days, string message)
    {
        bool rejected = false;
        try
        {
            CreateCalendar(days);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, message);
    }

    private void VerifyValidation(List<TradingCalendarDay> days)
    {
        List<TradingCalendarDay> missing = new List<TradingCalendarDay>(days);
        missing.RemoveAt(1);
        VerifyRejectedCalendar(missing, "A missing holiday declaration must be rejected.");
        List<TradingCalendarDay> duplicate = new List<TradingCalendarDay>(days);
        duplicate.Add(days[0]);
        VerifyRejectedCalendar(duplicate, "Duplicate dates must be rejected.");
        List<TradingCalendarDay> outside = new List<TradingCalendarDay>(days);
        outside[0] = new TradingCalendarDay(new DateOnly(2026, 9, 23), null);
        VerifyRejectedCalendar(outside, "Out-of-coverage entries must be rejected.");
        TradingSession original = days[0].Session!;
        List<TradingCalendarDay> wrongInstrument = new List<TradingCalendarDay>(days);
        wrongInstrument[0] = new TradingCalendarDay(days[0].Date,
            new TradingSession(999, original.OpenedAt, original.ClosedAt));
        VerifyRejectedCalendar(wrongInstrument, "A session for a different instrument must be rejected.");
        bool rejected = false;
        try
        {
            new TradingCalendarDay(days[1].Date, original);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "Session and declared date must agree in IST.");
        rejected = false;
        try
        {
            new TradingCalendarDay(days[0].Date, new TradingSession(265, original.OpenedAt, original.ClosedAt.AddDays(1)));
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "Unsupported overnight sessions must fail explicitly.");
        days.Reverse();
        TradingSessionCalendar unordered = CreateCalendar(days);
        Assert(unordered.GetNextSession(265, original.ClosedAt).Session!.OpenedAt
            == DateTimeOffset.Parse("2026-09-26T18:00:00+05:30", CultureInfo.InvariantCulture),
            "Input ordering must not affect chronological lookup.");
    }

    private void VerifyClockIntegration(ITradingSessionCalendar calendar)
    {
        TradingSession? session = calendar.GetSession(265, new DateOnly(2026, 9, 26)).Session;
        if (session == null)
        {
            throw new InvalidOperationException("Exceptional-session fixture missing.");
        }
        IntradayCandleClock clock = new IntradayCandleClock();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-26T19:00:00+05:30", CultureInfo.InvariantCulture);
        Assert(clock.GetCompletion(start, TimeSpan.FromMinutes(20), session) == session.ClosedAt,
            "Clock must honor the calendar's exceptional close.");
        Candle candle = new Candle(265, "20 minute", MarketTimestamp.ToDatabase(start), 100, 101, 99, 100, 10);
        CompletedCandle? completed;
        Assert(!clock.TryCreateCompleted(candle, TimeSpan.FromMinutes(20), session, session.ClosedAt.AddTicks(-1), true, out completed),
            "Exceptional candle must remain unavailable before close.");
        Assert(clock.TryCreateCompleted(candle, TimeSpan.FromMinutes(20), session, session.ClosedAt, true, out completed),
            "Finalized exceptional candle must be available at close.");
        Assert(calendar.GetNextSession(265, session.ClosedAt).Session!.OpenedAt
            == DateTimeOffset.Parse("2026-09-28T10:00:00+05:30", CultureInfo.InvariantCulture),
            "Next activation must skip the supplied closed date.");
    }
}
