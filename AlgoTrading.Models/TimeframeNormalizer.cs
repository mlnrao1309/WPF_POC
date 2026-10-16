using System;
using System.Globalization;

namespace AlgoTrading.Models
{
    public sealed class TimeframeNormalizer
    {
        public TimeframeDefinition Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("A timeframe is required.", nameof(input));
            }

            string value = input.Trim().ToLowerInvariant();
            if (value == "d" || value == "day" || value == "daily")
            {
                return new TimeframeDefinition("D", TimeframeKind.Daily, null);
            }
            if (value == "w" || value == "week" || value == "weekly")
            {
                return new TimeframeDefinition("W", TimeframeKind.Weekly, null);
            }
            if (value == "m" || value == "month" || value == "monthly")
            {
                return new TimeframeDefinition("M", TimeframeKind.Monthly, null);
            }

            int minutes = ParseIntradayMinutes(value);
            TimeSpan duration = TimeSpan.FromMinutes(minutes);
            return new TimeframeDefinition(BuildIntradayName(minutes), TimeframeKind.Intraday, duration);
        }

        private int ParseIntradayMinutes(string value)
        {
            string numericPart = value;
            string suffix = string.Empty;
            int firstLetter = FindFirstLetter(value);
            if (firstLetter >= 0)
            {
                numericPart = value.Substring(0, firstLetter).Trim();
                suffix = value.Substring(firstLetter).Trim();
            }
            if (!int.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                || minutes <= 0)
            {
                throw new FormatException("An intraday timeframe must begin with a positive whole number of minutes.");
            }
            if (suffix.Length == 0 || suffix == "m" || suffix == "min" || suffix == "mins" || suffix == "minute" || suffix == "minutes")
            {
                return minutes;
            }
            if (suffix == "h" || suffix == "hr" || suffix == "hour" || suffix == "hours")
            {
                if (minutes > int.MaxValue / 60)
                {
                    throw new FormatException("The timeframe duration is too large.");
                }
                return minutes * 60;
            }
            throw new FormatException("Unknown intraday timeframe unit: " + suffix);
        }

        private int FindFirstLetter(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsLetter(value[index]))
                {
                    return index;
                }
            }
            return -1;
        }

        private string BuildIntradayName(int minutes)
        {
            return minutes.ToString(CultureInfo.InvariantCulture) + "minute";
        }
    }
}
