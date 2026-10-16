namespace AlgoTrading.DataAccess.Calendar
{
    public sealed class TradingCalendarSelection
    {
        public TradingCalendarSelection(string exchangeCode, string segmentCode, string revision)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(segmentCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(revision);
            ExchangeCode = exchangeCode;
            SegmentCode = segmentCode;
            Revision = revision;
        }

        public string ExchangeCode { get; }
        public string SegmentCode { get; }
        public string Revision { get; }
    }
}
