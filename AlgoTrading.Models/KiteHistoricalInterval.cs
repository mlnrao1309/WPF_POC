namespace AlgoTrading.Models
{
    /// <summary>
    /// Zerodha historical interval identifiers. Kite names the hourly interval "60minute";
    /// the application keeps "hour" only as an internal label.
    /// </summary>
    public static class KiteHistoricalInterval
    {
        public const string Minute = "minute";
        public const string FiveMinute = "5minute";
        public const string FifteenMinute = "15minute";
        public const string SixtyMinute = "60minute";
        public const string Daily = "day";
    }
}
