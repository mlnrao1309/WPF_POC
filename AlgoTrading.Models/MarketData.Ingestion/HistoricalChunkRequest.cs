namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>One inclusive provider range. It is persisted atomically with its checkpoint.</summary>
    public sealed class HistoricalChunkRequest
    {
        public HistoricalChunkRequest(long instrumentToken, string streamType, DateTime rangeStart, DateTime rangeEnd)
            : this(instrumentToken, streamType, rangeStart, rangeEnd, null, null)
        {
        }

        public HistoricalChunkRequest(long instrumentToken, string streamType, DateTime rangeStart, DateTime rangeEnd,
            DateTime? candleStart, DateTime? candleEnd)
        {
            if (instrumentToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instrumentToken));
            }

            HistoricalStreamTypes.Validate(streamType);
            if (rangeEnd < rangeStart)
            {
                throw new ArgumentException("Historical chunk end must not precede its start.", nameof(rangeEnd));
            }

            if (candleStart.HasValue != candleEnd.HasValue
                || candleStart.HasValue && candleEnd!.Value <= candleStart.Value)
            {
                throw new ArgumentException("Candle bounds must be supplied together in ascending order.");
            }

            this.InstrumentToken = instrumentToken;
            this.StreamType = streamType;
            this.RangeStart = rangeStart;
            this.RangeEnd = rangeEnd;
            this.CandleStart = candleStart;
            this.CandleEnd = candleEnd;
        }

        public long InstrumentToken { get; }

        public string StreamType { get; }

        public string ProviderInterval
        {
            get
            {
                return HistoricalStreamTypes.GetProviderInterval(this.StreamType);
            }
        }

        public DateTime RangeStart { get; }

        public DateTime RangeEnd { get; }

        public DateTime? CandleStart { get; }

        public DateTime? CandleEnd { get; }
    }
}
