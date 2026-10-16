namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>Authoritative provider source streams used by historical and live market-data processing.</summary>
    public static class HistoricalStreamTypes
    {
        public const string IntradayOneMinute = "INTRADAY_1M";

        public const string IntradayFiveMinute = "INTRADAY_5M";

        public const string IntradayFifteenMinute = "INTRADAY_15M";

        public const string IntradayOneHour = "INTRADAY_1H";

        public const string DailyOneDay = "DAILY_1D";

        public static string GetProviderInterval(string streamType)
        {
            if (streamType == IntradayOneMinute)
            {
                return "minute";
            }

            if (streamType == IntradayFiveMinute)
            {
                return "5minute";
            }

            if (streamType == DailyOneDay)
            {
                return "day";
            }

            if (streamType == IntradayFifteenMinute)
            {
                return "15minute";
            }

            if (streamType == IntradayOneHour)
            {
                return "60minute";
            }

            throw new ArgumentException("Unsupported historical source stream: " + streamType + ".", nameof(streamType));
        }

        public static string GetDatabaseTimeframe(string streamType)
        {
            if (streamType == IntradayOneMinute)
            {
                return "1";
            }

            if (streamType == IntradayFiveMinute)
            {
                return "5";
            }

            if (streamType == DailyOneDay)
            {
                return "D";
            }

            if (streamType == IntradayFifteenMinute)
            {
                return "15";
            }

            if (streamType == IntradayOneHour)
            {
                return "60";
            }

            throw new ArgumentException("Unsupported historical source stream: " + streamType + ".", nameof(streamType));
        }

        public static int GetChunkDays(string streamType)
        {
            if (streamType == IntradayOneMinute)
            {
                return 30;
            }

            if (streamType == IntradayFiveMinute)
            {
                return 90;
            }

            if (streamType == DailyOneDay)
            {
                return 365;
            }

            if (streamType == IntradayFifteenMinute)
            {
                return 90;
            }

            if (streamType == IntradayOneHour)
            {
                return 180;
            }

            throw new ArgumentException("Unsupported historical source stream: " + streamType + ".", nameof(streamType));
        }

        public static void Validate(string streamType)
        {
            GetProviderInterval(streamType);
        }
    }
}
