using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models
{
    /// <summary>
    /// Bridges the three timeframe vocabularies used by the platform:
    /// the configuration code ('5m', '75m', '1W'), the canonical model name ('75minute', 'W')
    /// and the compact database code stored in Instruments_OHLC.TimeFrame ('75', 'W').
    /// A timeframe can be deactivated in configuration without code deployment.
    /// </summary>
    public sealed class TimeframeCatalog
    {
        private readonly Dictionary<string, TimeframeDefinition> byCode;
        private readonly Dictionary<string, TimeframeDefinition> byCanonicalName;
        private readonly Dictionary<string, TimeframeOption> optionsByCode;

        public TimeframeCatalog(IReadOnlyList<TimeframeOption> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Count == 0)
            {
                throw new ArgumentException("At least one timeframe option is required.", nameof(options));
            }

            this.byCode = new Dictionary<string, TimeframeDefinition>(StringComparer.OrdinalIgnoreCase);
            this.byCanonicalName = new Dictionary<string, TimeframeDefinition>(StringComparer.OrdinalIgnoreCase);
            this.optionsByCode = new Dictionary<string, TimeframeOption>(StringComparer.OrdinalIgnoreCase);
            List<string> activeCodes = new List<string>();
            foreach (TimeframeOption option in options)
            {
                if (option == null)
                {
                    throw new ArgumentException("Timeframe options cannot be null.", nameof(options));
                }

                TimeframeDefinition definition = new TimeframeNormalizer().Normalize(option.CanonicalName);
                if (!this.byCode.TryAdd(option.Code, definition))
                {
                    throw new ArgumentException("Duplicate timeframe code: " + option.Code, nameof(options));
                }

                this.byCanonicalName[definition.CanonicalName] = definition;
                this.optionsByCode[option.Code] = option;
                if (option.IsActive)
                {
                    activeCodes.Add(option.Code);
                }
            }

            this.ActiveCodes = activeCodes.AsReadOnly();
        }

        /// <summary>The blueprint's default universe: five intraday and three calendar timeframes.</summary>
        public static TimeframeCatalog CreateDefault()
        {
            List<TimeframeOption> options = new List<TimeframeOption>();
            options.Add(new TimeframeOption("5m", "5minute", 5, TimeframeSourceStream.IntradayFiveMinute, true));
            options.Add(new TimeframeOption("15m", "15minute", 15, TimeframeSourceStream.IntradayFiveMinute, true));
            options.Add(new TimeframeOption("25m", "25minute", 25, TimeframeSourceStream.IntradayFiveMinute, true));
            options.Add(new TimeframeOption("60m", "60minute", 60, TimeframeSourceStream.IntradayFiveMinute, true));
            options.Add(new TimeframeOption("75m", "75minute", 75, TimeframeSourceStream.IntradayFiveMinute, true));
            options.Add(new TimeframeOption("1D", "D", 0, TimeframeSourceStream.DailyOneDay, true));
            options.Add(new TimeframeOption("1W", "W", 0, TimeframeSourceStream.DailyOneDay, true));
            options.Add(new TimeframeOption("1M", "M", 0, TimeframeSourceStream.DailyOneDay, true));
            return new TimeframeCatalog(options);
        }

        /// <summary>Configuration codes for timeframes that are currently enabled.</summary>
        public IReadOnlyList<string> ActiveCodes { get; private set; }

        public bool TryResolve(string code, out TimeframeDefinition? definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return this.byCode.TryGetValue(code, out definition);
        }

        public bool TryResolveCanonicalName(string canonicalName, out TimeframeDefinition? definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                return false;
            }

            return this.byCanonicalName.TryGetValue(canonicalName, out definition);
        }

        public TimeframeOption GetOption(string code)
        {
            TimeframeOption? option;
            if (!this.optionsByCode.TryGetValue(code, out option) || option == null)
            {
                throw new KeyNotFoundException("Unknown timeframe code: " + code);
            }

            return option;
        }

        /// <summary>Returns only the enabled definitions so a disabled timeframe halts calculation.</summary>
        public IReadOnlyList<TimeframeDefinition> ActiveDefinitions()
        {
            List<TimeframeDefinition> result = new List<TimeframeDefinition>();
            for (int index = 0; index < this.ActiveCodes.Count; index++)
            {
                TimeframeDefinition definition = this.byCode[this.ActiveCodes[index]];
                result.Add(definition);
            }

            return result.AsReadOnly();
        }

        public string ToDatabaseCode(string code)
        {
            return this.GetOption(code).DatabaseCode;
        }

        /// <summary>Maps a configuration code to the Kite historical interval it must be fetched with.</summary>
        public string ToKiteInterval(string code)
        {
            TimeframeOption option = this.GetOption(code);
            if (option.SourceStream == TimeframeSourceStream.DailyOneDay)
            {
                return KiteHistoricalInterval.Daily;
            }

            return option.Minutes.ToString(CultureInfo.InvariantCulture) + "minute";
        }
    }
}
