using System;

namespace AlgoTrading.Models
{
    public readonly struct Candle
    {
        private readonly int storedInstrumentToken;

        public int InstrumentToken
        {
            get
            {
                return this.storedInstrumentToken;
            }
        }
        private readonly string storedTimeframeMinutes;

        public string TimeframeMinutes
        {
            get
            {
                return this.storedTimeframeMinutes;
            }
        }
        private readonly DateTime sourceTimestamp;
        private readonly DateTimeOffset openedAt;

        public DateTime SourceTimestamp
        {
            get
            {
                return this.sourceTimestamp;
            }
        }

        public DateTimeOffset OpenedAt
        {
            get
            {
                return this.openedAt;
            }
        }

        public DateTime Timestamp
        {
            get
            {
                return this.openedAt.UtcDateTime;
            }
        }
        private readonly decimal storedOpen;

        public decimal Open
        {
            get
            {
                return this.storedOpen;
            }
        }
        private readonly decimal storedHigh;

        public decimal High
        {
            get
            {
                return this.storedHigh;
            }
        }
        private readonly decimal storedLow;

        public decimal Low
        {
            get
            {
                return this.storedLow;
            }
        }
        private readonly decimal storedClose;

        public decimal Close
        {
            get
            {
                return this.storedClose;
            }
        }
        private readonly long storedVolume;

        public long Volume
        {
            get
            {
                return this.storedVolume;
            }
        }

        private readonly long storedOpenInterest;

        public long OI
        {
            get
            {
                return this.storedOpenInterest;
            }
        }

        public Candle(int instrumentToken, string timeframeMinutes, DateTime timestamp, decimal open, decimal high,
            decimal low, decimal close, long volume, long oi = 0)
        {
            this.storedInstrumentToken = instrumentToken;
            this.storedTimeframeMinutes = timeframeMinutes;
            this.sourceTimestamp = timestamp;
            this.openedAt = MarketTimestamp.FromDateTime(timestamp);
            this.storedOpen = open;
            this.storedHigh = high;
            this.storedLow = low;
            this.storedClose = close;
            this.storedVolume = volume;
            this.storedOpenInterest = oi;
        }
    }
}
