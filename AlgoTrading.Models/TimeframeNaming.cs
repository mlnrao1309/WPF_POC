using System;
using System.Globalization;

namespace AlgoTrading.Models
{
    /// <summary>Single place that translates the configuration code vocabulary into canonical timeframe names.</summary>
    public static class TimeframeNaming
    {
        public static string DeriveCanonicalName(string configurationCode)
        {
            if (string.IsNullOrWhiteSpace(configurationCode))
            {
                throw new ArgumentException("A configuration code is required.", nameof(configurationCode));
            }

            string code = configurationCode.Trim();
            if (code.Equals("1D", StringComparison.OrdinalIgnoreCase) || code.Equals("D", StringComparison.OrdinalIgnoreCase))
            {
                return "D";
            }

            if (code.Equals("1W", StringComparison.OrdinalIgnoreCase) || code.Equals("W", StringComparison.OrdinalIgnoreCase))
            {
                return "W";
            }

            if (code.Equals("1M", StringComparison.OrdinalIgnoreCase) || code.Equals("M", StringComparison.OrdinalIgnoreCase))
            {
                return "M";
            }

            int letterIndex = FindFirstLetter(code);
            string numericPart = letterIndex < 0 ? code : code.Substring(0, letterIndex);
            int minutes;
            if (!int.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes <= 0)
            {
                throw new FormatException("Unknown timeframe configuration code: " + configurationCode);
            }

            return minutes.ToString(CultureInfo.InvariantCulture) + "minute";
        }

        private static int FindFirstLetter(string value)
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
    }
}
