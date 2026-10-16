using System;

namespace AlgoTrading.Models
{
    public enum TimeframeSourceStream
    {
        IntradayFiveMinute,
        DailyOneDay
    }

    /// <summary>One Config_Timeframes row: the configuration code, its canonical model name and its source stream.</summary>
    public sealed class TimeframeOption
    {
        public TimeframeOption(string code, string canonicalName, int minutes, TimeframeSourceStream sourceStream, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A configuration code is required.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                throw new ArgumentException("A canonical timeframe name is required.", nameof(canonicalName));
            }

            if (minutes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minutes));
            }

            this.Code = code;
            this.CanonicalName = canonicalName;
            this.Minutes = minutes;
            this.SourceStream = sourceStream;
            this.IsActive = isActive;
        }

        public string Code { get; }

        public string CanonicalName { get; }

        public int Minutes { get; }

        public TimeframeSourceStream SourceStream { get; }

        public bool IsActive { get; }

        /// <summary>Compact code persisted in Instruments_OHLC.TimeFrame and referenced by configuration rows.</summary>
        public string DatabaseCode
        {
            get
            {
                if (this.Minutes != 0)
                {
                    return this.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (this.CanonicalName == "W")
                {
                    return "W";
                }

                if (this.CanonicalName == "M")
                {
                    return "M";
                }

                return "D";
            }
        }
    }
}
