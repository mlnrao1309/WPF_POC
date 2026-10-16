using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class ExchangeCalendarSnapshot
    {
        private readonly Guid calendarId;
        private readonly string exchangeCode;
        private readonly string segmentCode;
        private readonly string revision;
        private readonly DateOnly firstDate;
        private readonly DateOnly lastDate;
        private readonly Guid sourceId;
        private readonly IReadOnlyList<ExchangeCalendarDay> days;

        public ExchangeCalendarSnapshot(Guid calendarId, string exchangeCode, string segmentCode, string revision, DateOnly firstDate, DateOnly lastDate, Guid sourceId, IReadOnlyList<ExchangeCalendarDay> days)
        {
            if (calendarId == Guid.Empty || sourceId == Guid.Empty)
            {
                throw new ArgumentException("Calendar and source identities are required.");
            }
            if (string.IsNullOrWhiteSpace(exchangeCode) || exchangeCode.Length > 16 || string.IsNullOrWhiteSpace(segmentCode) || segmentCode.Length > 32
                || string.IsNullOrWhiteSpace(revision) || revision.Length > 80)
            {
                throw new ArgumentException("Valid exchange, segment and revision identifiers are required.");
            }
            if (lastDate < firstDate || days == null)
            {
                throw new ArgumentException("Calendar coverage and days are required.");
            }
            SortedDictionary<DateOnly, ExchangeCalendarDay> ordered = new SortedDictionary<DateOnly, ExchangeCalendarDay>();
            foreach (ExchangeCalendarDay day in days)
            {
                if (day == null || day.Date < firstDate || day.Date > lastDate || !ordered.TryAdd(day.Date, day))
                {
                    throw new ArgumentException("Calendar dates must be unique and within coverage.");
                }
            }
            if (ordered.Count != lastDate.DayNumber - firstDate.DayNumber + 1)
            {
                throw new ArgumentException("Every date must explicitly contain a session or closure.");
            }
            days = new List<ExchangeCalendarDay>(ordered.Values).AsReadOnly();
            this.calendarId = calendarId;
            this.exchangeCode = exchangeCode;
            this.segmentCode = segmentCode;
            this.revision = revision;
            this.firstDate = firstDate;
            this.lastDate = lastDate;
            this.sourceId = sourceId;
            this.days = days;
        }

        public Guid CalendarId
        {
            get
            {
                return this.calendarId;
            }
        }

        public string ExchangeCode
        {
            get
            {
                return this.exchangeCode;
            }
        }

        public string SegmentCode
        {
            get
            {
                return this.segmentCode;
            }
        }

        public string Revision
        {
            get
            {
                return this.revision;
            }
        }

        public DateOnly FirstDate
        {
            get
            {
                return this.firstDate;
            }
        }

        public DateOnly LastDate
        {
            get
            {
                return this.lastDate;
            }
        }

        public Guid SourceId
        {
            get
            {
                return this.sourceId;
            }
        }

        public IReadOnlyList<ExchangeCalendarDay> Days
        {
            get
            {
                return this.days;
            }
        }

        public TradingSessionCalendar ForInstrument(int instrumentToken)
        {
            List<TradingCalendarDay> calendarDays = new List<TradingCalendarDay>();
            foreach (ExchangeCalendarDay day in this.days)
            {
                TradingSession? session = null;
                if (day.OpensAt.HasValue && day.ClosesAt.HasValue)
                {
                    DateTimeOffset start = MarketTimestamp.FromDatabase(day.Date.ToDateTime(day.OpensAt.Value));
                    DateTimeOffset end = MarketTimestamp.FromDatabase(day.Date.ToDateTime(day.ClosesAt.Value));
                    session = new TradingSession(instrumentToken, start, end);
                }
                calendarDays.Add(new TradingCalendarDay(day.Date, session));
            }
            return new TradingSessionCalendar(instrumentToken, this.firstDate, this.lastDate, this.revision, calendarDays);
        }
    }
}
