namespace AlgoTrading.Models
{
    public sealed class CalendarLookupResult
    {
        private readonly CalendarLookupStatus status;
        private readonly TradingSession? session;

        internal CalendarLookupResult(CalendarLookupStatus status, TradingSession? session)
        {
            this.status = status;
            this.session = session;
        }

        public CalendarLookupStatus Status
        {
            get
            {
                return this.status;
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
