using AlgoTrading.Models;
using AlgoTrading.Models.Rules;

internal sealed class CalendarCandleAggregationTests
{
    private int assertionCount;

    internal void Run()
    {
        TimeframeNormalizer normalizer = new TimeframeNormalizer();
        TradingSessionCalendar weeklyCalendar = CreateCalendar(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 3);
        IReadOnlyList<CompletedCandle> weeklySource = BuildDailyCandles(weeklyCalendar, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27));
        CalendarCandleAggregator aggregator = new CalendarCandleAggregator();
        CalendarAggregatedCandle weekly = aggregator.Aggregate(weeklySource, normalizer.Normalize("W"), weeklyCalendar, 265)[0];
        Assert(weekly.FirstConstituent.Candle.OpenedAt == weeklySource[0].Candle.OpenedAt, "Weekly first constituent was not retained.");
        Assert(weekly.LastConstituent.Candle.OpenedAt == weeklySource[3].Candle.OpenedAt, "Weekly last constituent was not retained.");
        Assert(weekly.Aggregate.Candle.Open == 21 && weekly.Aggregate.Candle.Close == 24, "Weekly OHLC endpoints are wrong.");
        Assert(weekly.Aggregate.Candle.High == 25 && weekly.Aggregate.Candle.Low == 20, "Weekly OHLC range is wrong.");
        Assert(weekly.Aggregate.Candle.Volume == 4 * 100, "Weekly volume is wrong.");
        Assert(weekly.Aggregate.ClosedAt == weekly.LastConstituent.ClosedAt, "Weekly completion must be the last session close.");
        VerifyMissingSource(weeklySource, weeklyCalendar, normalizer.Normalize("W"));

        TradingSessionCalendar monthlyCalendar = CreateCalendar(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 7);
        IReadOnlyList<CompletedCandle> monthlySource = BuildDailyCandles(monthlyCalendar, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        CalendarAggregatedCandle monthly = aggregator.Aggregate(monthlySource, normalizer.Normalize("M"), monthlyCalendar, 265)[0];
        Assert(monthly.PeriodStart == new DateOnly(2026, 9, 1) && monthly.PeriodEnd == new DateOnly(2026, 9, 30), "Monthly period boundaries are wrong.");
        Assert(monthly.LastConstituent.Candle.OpenedAt == monthlySource[monthlySource.Count - 1].Candle.OpenedAt, "Monthly last constituent was not retained.");
        Assert(monthly.Aggregate.ClosedAt == monthly.LastConstituent.ClosedAt, "Monthly completion must be the final trading session close.");
        VerifyCalendarTargetRejected(monthlySource, monthlyCalendar);
        Console.WriteLine("Passed " + assertionCount + " calendar aggregation assertions.");
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private TradingSessionCalendar CreateCalendar(DateOnly firstDate, DateOnly lastDate, int closedWeekday)
    {
        List<TradingCalendarDay> days = new List<TradingCalendarDay>();
        DateOnly date = firstDate;
        while (date <= lastDate)
        {
            TradingSession? session = null;
            if ((int)date.DayOfWeek != closedWeekday && date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                DateTime start = date.ToDateTime(new TimeOnly(9, 15));
                DateTime close = date.ToDateTime(new TimeOnly(15, 30));
                session = new TradingSession(265, MarketTimestamp.FromDatabase(start), MarketTimestamp.FromDatabase(close));
            }
            days.Add(new TradingCalendarDay(date, session));
            date = date.AddDays(1);
        }
        return new TradingSessionCalendar(265, firstDate, lastDate, "aggregation-calendar", days);
    }

    private IReadOnlyList<CompletedCandle> BuildDailyCandles(ITradingSessionCalendar calendar, DateOnly firstDate, DateOnly lastDate)
    {
        List<CompletedCandle> result = new List<CompletedCandle>();
        DateOnly date = firstDate;
        int value = date.Day;
        while (date <= lastDate)
        {
            CalendarLookupResult lookup = calendar.GetSession(265, date);
            if (lookup.Status == CalendarLookupStatus.Found && lookup.Session != null)
            {
                decimal close = value;
                Candle candle = new Candle(265, "D", MarketTimestamp.ToDatabase(lookup.Session.OpenedAt),
                    close, close + 1, close - 1, close, 100);
                result.Add(new CompletedCandle(candle, lookup.Session.ClosedAt));
                value++;
            }
            date = date.AddDays(1);
        }
        return result.AsReadOnly();
    }

    private void VerifyMissingSource(IReadOnlyList<CompletedCandle> source, ITradingSessionCalendar calendar, TimeframeDefinition target)
    {
        List<CompletedCandle> missing = new List<CompletedCandle>(source);
        missing.RemoveAt(1);
        bool rejected = false;
        try
        {
            new CalendarCandleAggregator().Aggregate(missing, target, calendar, 265);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Missing trading session must invalidate the period.");
    }

    private void VerifyCalendarTargetRejected(IReadOnlyList<CompletedCandle> source, ITradingSessionCalendar calendar)
    {
        bool rejected = false;
        try
        {
            new CalendarCandleAggregator().Aggregate(source, new TimeframeNormalizer().Normalize("15m"), calendar, 265);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Assert(rejected, "Intraday target must not enter calendar aggregation.");
    }
}
