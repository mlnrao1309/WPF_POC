using System;

namespace AlgoTrading.Models
{
    /// <summary>An explicit trading interval supplied by the calendar/provider, not an inferred weekday.</summary>
    public sealed class TradingSession
    {
        private readonly int instrumentToken;
        private readonly DateTimeOffset openedAt;
        private readonly DateTimeOffset closedAt;

        public TradingSession(int instrumentToken, DateTimeOffset openedAt, DateTimeOffset closedAt)
        {
            if (closedAt <= openedAt)
            {
                throw new ArgumentException("Session close must follow session open.", nameof(closedAt));
            }

            this.instrumentToken = instrumentToken;
            this.openedAt = MarketTimestamp.FromDateTime(openedAt.UtcDateTime);
            this.closedAt = MarketTimestamp.FromDateTime(closedAt.UtcDateTime);
        }

        public int InstrumentToken
        {
            get
            {
                return this.instrumentToken;
            }
        }

        public DateTimeOffset OpenedAt
        {
            get
            {
                return this.openedAt;
            }
        }

        public DateTimeOffset ClosedAt
        {
            get
            {
                return this.closedAt;
            }
        }
    }
}
