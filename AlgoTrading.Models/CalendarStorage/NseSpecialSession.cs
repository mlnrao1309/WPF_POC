namespace AlgoTrading.Models
{
    /// <summary>A non-standard NSE session announced in an official exchange circular.</summary>
    public sealed class NseSpecialSession
    {
        public NseSpecialSession(DateOnly date, TimeOnly opensAt, TimeOnly closesAt, string reason,
            CalendarSourceDocument source)
        {
            if (opensAt >= closesAt)
            {
                throw new ArgumentException("Special-session close must follow open.");
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A special-session reason is required.", nameof(reason));
            }
            ArgumentNullException.ThrowIfNull(source);
            bool officialHost = string.Equals(source.SourceUri.Host, "www.nseindia.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source.SourceUri.Host, "nsearchives.nseindia.com", StringComparison.OrdinalIgnoreCase)
                || source.SourceUri.Host.EndsWith(".nseindia.com", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(source.ExchangeCode, "NSE", StringComparison.Ordinal) || !officialHost)
            {
                throw new ArgumentException("Special-session provenance must be an official NSE source.", nameof(source));
            }

            Date = date;
            OpensAt = opensAt;
            ClosesAt = closesAt;
            Reason = reason;
            Source = source;
        }

        public DateOnly Date { get; }
        public TimeOnly OpensAt { get; }
        public TimeOnly ClosesAt { get; }
        public string Reason { get; }
        public CalendarSourceDocument Source { get; }
    }
}
