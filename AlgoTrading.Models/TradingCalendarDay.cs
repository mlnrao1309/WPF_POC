using System;

namespace AlgoTrading.Models
{
    /// <summary>A null session explicitly declares a closed date, not missing calendar data.</summary>
    public sealed class TradingCalendarDay
    {
        private readonly DateOnly date;
        private readonly TradingSession? session;

        public TradingCalendarDay(DateOnly date, TradingSession? session)
        {
            if (session != null)
            {
                DateTime start = MarketTimestamp.ToDatabase(session.OpenedAt);
                DateTime end = MarketTimestamp.ToDatabase(session.ClosedAt);
                if (DateOnly.FromDateTime(start) != date || DateOnly.FromDateTime(end) != date)
                {
                    throw new ArgumentException("This calendar requires a continuous session within its declared IST date.", nameof(session));
                }
            }
            this.date = date;
            this.session = session;
        }

        public DateOnly Date
        {
            get
            {
                return this.date;
            }
        }

        public TradingSession? Session
        {
            get
            {
                return this.session;
            }
        }
    }
}
