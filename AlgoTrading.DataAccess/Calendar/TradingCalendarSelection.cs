namespace AlgoTrading.DataAccess.Calendar
{
    public sealed class TradingCalendarSelection
    {
        public TradingCalendarSelection(string exchangeCode, string segmentCode, string revision, DateOnly? sessionBoundsFrom = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(segmentCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(revision);
            ExchangeCode = exchangeCode;
            SegmentCode = segmentCode;
            Revision = revision;
            SessionBoundsFrom = sessionBoundsFrom ?? new DateOnly(2026, 8, 3);
        }

        public string ExchangeCode { get; }
        public string SegmentCode { get; }
        public string Revision { get; }
        public DateOnly SessionBoundsFrom { get; }
    }
}
