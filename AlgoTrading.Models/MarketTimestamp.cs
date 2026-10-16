using System;

namespace AlgoTrading.Models
{
    /// <summary>Converts the application's IST database wall times and explicit UTC instants.</summary>
    public static class MarketTimestamp
    {
        private static readonly TimeSpan indiaOffset = TimeSpan.FromMinutes(330);

        public static DateTimeOffset FromDatabase(DateTime timestamp)
        {
            if (timestamp.Kind != DateTimeKind.Unspecified)
            {
                throw new ArgumentException("A database timestamp must be an unspecified IST wall time.", nameof(timestamp));
            }

            return new DateTimeOffset(timestamp, indiaOffset);
        }

        public static DateTimeOffset FromDateTime(DateTime timestamp)
        {
            if (timestamp.Kind == DateTimeKind.Utc)
            {
                return new DateTimeOffset(timestamp).ToOffset(indiaOffset);
            }

            if (timestamp.Kind == DateTimeKind.Local)
            {
                throw new ArgumentException("Use an explicit UTC timestamp instead of a machine-local DateTime.", nameof(timestamp));
            }

            return FromDatabase(timestamp);
        }

        public static DateTime ToDatabase(DateTimeOffset instant)
        {
            return instant.ToOffset(indiaOffset).DateTime;
        }
    }
}
