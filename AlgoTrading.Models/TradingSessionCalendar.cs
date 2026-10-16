using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    /// <summary>An immutable, fully covered calendar snapshot for one instrument. No weekday assumptions.</summary>
    public sealed class TradingSessionCalendar : ITradingSessionCalendar
    {
        private readonly int instrumentToken;
        private readonly DateOnly firstDate;
        private readonly DateOnly lastDate;
        private readonly string revision;
        private readonly Dictionary<DateOnly, TradingCalendarDay> days;

        public TradingSessionCalendar(int instrumentToken, DateOnly firstDate, DateOnly lastDate,
            string revision, IReadOnlyList<TradingCalendarDay> days)
        {
            if (lastDate < firstDate)
            {
                throw new ArgumentException("Calendar end must not precede its start.", nameof(lastDate));
            }
            if (string.IsNullOrWhiteSpace(revision))
            {
                throw new ArgumentException("A calendar revision is required.", nameof(revision));
            }
            if (days == null)
            {
                throw new ArgumentNullException(nameof(days));
            }
            this.instrumentToken = instrumentToken;
            this.firstDate = firstDate;
            this.lastDate = lastDate;
            this.revision = revision;
            this.days = new Dictionary<DateOnly, TradingCalendarDay>();
            foreach (TradingCalendarDay day in days)
            {
                if (day == null)
                {
                    throw new ArgumentException("Calendar entries cannot be null.", nameof(days));
                }
                if (day.Date < firstDate || day.Date > lastDate)
                {
                    throw new ArgumentException("Calendar entry lies outside declared coverage.", nameof(days));
                }
                if (day.Session != null && day.Session.InstrumentToken != instrumentToken)
                {
                    throw new ArgumentException("Calendar session belongs to a different instrument.", nameof(days));
                }
                if (!this.days.TryAdd(day.Date, day))
                {
                    throw new ArgumentException("Calendar contains a duplicate date.", nameof(days));
                }
            }
            int expectedDays = lastDate.DayNumber - firstDate.DayNumber + 1;
            if (this.days.Count != expectedDays)
            {
                throw new ArgumentException("Every covered date must explicitly contain a session or be closed; missing dates are not holidays.", nameof(days));
            }
        }

        public string Revision
        {
            get
            {
                return this.revision;
            }
        }

        public CalendarLookupResult GetSession(int instrumentToken, DateOnly date)
        {
            if (instrumentToken != this.instrumentToken || date < this.firstDate || date > this.lastDate)
            {
                return new CalendarLookupResult(CalendarLookupStatus.Unavailable, null);
            }
            TradingSession? session = this.days[date].Session;
            if (session == null)
            {
                return new CalendarLookupResult(CalendarLookupStatus.ClosedDay, null);
            }
            return new CalendarLookupResult(CalendarLookupStatus.Found, session);
        }

        /// <summary>Returns the first session whose opening is strictly after the supplied instant.</summary>
        public CalendarLookupResult GetNextSession(int instrumentToken, DateTimeOffset after)
        {
            DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(after));
            if (instrumentToken != this.instrumentToken || date < this.firstDate || date > this.lastDate)
            {
                return new CalendarLookupResult(CalendarLookupStatus.Unavailable, null);
            }
            for (int dayNumber = date.DayNumber; dayNumber <= this.lastDate.DayNumber; dayNumber++)
            {
                TradingSession? session = this.days[DateOnly.FromDayNumber(dayNumber)].Session;
                if (session != null && session.OpenedAt > after)
                {
                    return new CalendarLookupResult(CalendarLookupStatus.Found, session);
                }
            }
            // Coverage ends before a next opening is known; never imply that trading has ended forever.
            return new CalendarLookupResult(CalendarLookupStatus.Unavailable, null);
        }
    }
}
