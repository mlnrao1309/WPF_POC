using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlgoTrading.Models;
using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Models.MarketData.Ingestion;

namespace AlgoTrading.DataAccess.Infrastructure
{
    /// <summary>Converts database rows into the only configuration snapshot background processing may consume.</summary>
    public sealed class PlatformConfigurationValidator
    {
        public PlatformConfigurationSnapshot Activate(IReadOnlyList<ConfigTimeframe> timeframeRows,
            IReadOnlyList<ConfigCriticalLevel> criticalLevelRows)
        {
            if (timeframeRows == null)
            {
                throw new ArgumentNullException(nameof(timeframeRows));
            }

            if (criticalLevelRows == null)
            {
                throw new ArgumentNullException(nameof(criticalLevelRows));
            }

            if (timeframeRows.Count == 0)
            {
                throw new PlatformConfigurationException("Config_Timeframes is empty. Background processing is not configured.");
            }

            List<TimeframeOption> options = new List<TimeframeOption>();
            for (int index = 0; index < timeframeRows.Count; index++)
            {
                options.Add(this.ValidateTimeframe(timeframeRows[index]));
            }

            TimeframeCatalog catalog;
            try
            {
                catalog = new TimeframeCatalog(options);
            }
            catch (Exception exception)
            {
                throw new PlatformConfigurationException("Config_Timeframes contains conflicting rows.", exception);
            }

            List<ActiveCriticalLevelRule> activeRules = new List<ActiveCriticalLevelRule>();
            for (int index = 0; index < criticalLevelRows.Count; index++)
            {
                ConfigCriticalLevel row = criticalLevelRows[index];
                JsonDocument parameters = this.ValidateCriticalLevelBase(row, catalog);
                using (parameters)
                {
                    if (!row.IsActive)
                    {
                        continue;
                    }

                    this.ValidateActiveMethod(row, parameters.RootElement);
                    if (!catalog.GetOption(row.AppliedTimeframe).IsActive)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(row.ReferenceTimeframe)
                        && !catalog.GetOption(row.ReferenceTimeframe).IsActive)
                    {
                        continue;
                    }

                    activeRules.Add(new ActiveCriticalLevelRule(row.MethodCode, row.AppliedTimeframe,
                        row.ReferenceTimeframe, row.ParametersJson, row.MinimumBarsRequired));
                }
            }

            string revision = BuildRevision(timeframeRows, criticalLevelRows);
            return new PlatformConfigurationSnapshot(catalog, activeRules.AsReadOnly(), revision);
        }

        private TimeframeOption ValidateTimeframe(ConfigTimeframe row)
        {
            if (row == null)
            {
                throw new PlatformConfigurationException("Config_Timeframes contains a null row.");
            }

            string code = row.TimeframeCode;
            if (string.IsNullOrWhiteSpace(code) || code != code.Trim())
            {
                throw new PlatformConfigurationException("Config_Timeframes contains a non-canonical timeframe code.");
            }

            TimeframeSourceStream sourceStream;
            if (row.SourceStream == HistoricalStreamTypes.IntradayFiveMinute)
            {
                sourceStream = TimeframeSourceStream.IntradayFiveMinute;
            }
            else if (row.SourceStream == HistoricalStreamTypes.DailyOneDay)
            {
                sourceStream = TimeframeSourceStream.DailyOneDay;
            }
            else
            {
                throw new PlatformConfigurationException("Timeframe " + code + " has unsupported source stream "
                    + row.SourceStream + ".");
            }

            string canonicalName;
            try
            {
                canonicalName = TimeframeNaming.DeriveCanonicalName(code);
            }
            catch (Exception exception)
            {
                throw new PlatformConfigurationException("Timeframe " + code + " is not canonical.", exception);
            }

            if (canonicalName == "D" || canonicalName == "W" || canonicalName == "M")
            {
                string expectedCode = "1" + canonicalName;
                if (!string.Equals(code, expectedCode, StringComparison.Ordinal)
                    || row.MinutesMultiplier != 0
                    || sourceStream != TimeframeSourceStream.DailyOneDay)
                {
                    throw new PlatformConfigurationException("Calendar timeframe " + code
                        + " must use its canonical 1D/1W/1M code, zero minutes, and DAILY_1D.");
                }
            }
            else
            {
                int minutes = ParseCanonicalIntradayMinutes(canonicalName);
                string expectedCode = minutes.ToString(CultureInfo.InvariantCulture) + "m";
                if (!string.Equals(code, expectedCode, StringComparison.Ordinal)
                    || row.MinutesMultiplier != minutes
                    || minutes < 5
                    || minutes % 5 != 0
                    || sourceStream != TimeframeSourceStream.IntradayFiveMinute)
                {
                    throw new PlatformConfigurationException("Intraday timeframe " + code
                        + " must be a canonical positive five-minute multiple with matching minutes and INTRADAY_5M.");
                }
            }

            return new TimeframeOption(code, canonicalName, row.MinutesMultiplier, sourceStream, row.IsActive);
        }

        private JsonDocument ValidateCriticalLevelBase(ConfigCriticalLevel row, TimeframeCatalog catalog)
        {
            if (row == null)
            {
                throw new PlatformConfigurationException("Config_CriticalLevels contains a null row.");
            }

            if (string.IsNullOrWhiteSpace(row.MethodCode))
            {
                throw new PlatformConfigurationException("A critical-level method code is required.");
            }

            if (row.MinimumBarsRequired <= 0)
            {
                throw new PlatformConfigurationException("Rule " + row.MethodCode
                    + " must have a positive MinimumBarsRequired.");
            }

            TimeframeDefinition? appliedDefinition;
            if (!catalog.TryResolve(row.AppliedTimeframe, out appliedDefinition))
            {
                throw new PlatformConfigurationException("Rule " + row.MethodCode + " references unknown applied timeframe "
                    + row.AppliedTimeframe + ".");
            }

            if (!string.IsNullOrWhiteSpace(row.ReferenceTimeframe))
            {
                TimeframeDefinition? referenceDefinition;
                if (!catalog.TryResolve(row.ReferenceTimeframe, out referenceDefinition))
                {
                    throw new PlatformConfigurationException("Rule " + row.MethodCode
                        + " references unknown reference timeframe " + row.ReferenceTimeframe + ".");
                }
            }

            try
            {
                JsonDocument document = JsonDocument.Parse(row.ParametersJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new PlatformConfigurationException("Rule " + row.MethodCode
                        + " parameters must be a JSON object.");
                }

                return document;
            }
            catch (PlatformConfigurationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PlatformConfigurationException("Rule " + row.MethodCode
                    + " contains invalid ParametersJson.", exception);
            }
        }

        private void ValidateActiveMethod(ConfigCriticalLevel row, JsonElement parameters)
        {
            if (row.MethodCode == CalculatorMethodCodes.StandardPivot)
            {
                this.ValidatePivot(row, parameters);
                return;
            }

            if (row.MethodCode == CalculatorMethodCodes.EmaCrossover)
            {
                this.ValidateCrossover(row, parameters, "EMA");
                return;
            }

            if (row.MethodCode == CalculatorMethodCodes.SmaCrossover)
            {
                this.ValidateCrossover(row, parameters, "SMA");
                return;
            }

            throw new PlatformConfigurationException("Active calculator method " + row.MethodCode
                + " is not supported by the current background platform.");
        }

        private void ValidatePivot(ConfigCriticalLevel row, JsonElement parameters)
        {
            string? type = ReadRequiredString(parameters, "type", row.MethodCode);
            if (type != "SQL_RANGE_EXTENSION_V1")
            {
                throw new PlatformConfigurationException("PIVOT_STANDARD requires type SQL_RANGE_EXTENSION_V1.");
            }

            if (row.AppliedTimeframe != "1D" || row.ReferenceTimeframe != "1D")
            {
                throw new PlatformConfigurationException("PIVOT_STANDARD requires applied and reference timeframe 1D.");
            }
        }

        private void ValidateCrossover(ConfigCriticalLevel row, JsonElement parameters, string expectedFunction)
        {
            int fastLength = ReadRequiredPositiveInteger(parameters, "fastLength", row.MethodCode);
            int slowLength = ReadRequiredPositiveInteger(parameters, "slowLength", row.MethodCode);
            string? function = ReadRequiredString(parameters, "function", row.MethodCode);
            if (fastLength >= slowLength)
            {
                throw new PlatformConfigurationException(row.MethodCode
                    + " requires fastLength to be less than slowLength.");
            }

            if (!string.Equals(function, expectedFunction, StringComparison.Ordinal))
            {
                throw new PlatformConfigurationException(row.MethodCode + " requires function " + expectedFunction + ".");
            }

            if (row.MinimumBarsRequired < slowLength + 1)
            {
                throw new PlatformConfigurationException(row.MethodCode
                    + " MinimumBarsRequired must be at least slowLength plus one.");
            }
        }

        private static int ReadRequiredPositiveInteger(JsonElement parameters, string propertyName, string methodCode)
        {
            JsonElement value;
            int result;
            if (!parameters.TryGetProperty(propertyName, out value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out result)
                || result <= 0)
            {
                throw new PlatformConfigurationException(methodCode + " requires a positive integer " + propertyName + ".");
            }

            return result;
        }

        private static string ReadRequiredString(JsonElement parameters, string propertyName, string methodCode)
        {
            JsonElement value;
            if (!parameters.TryGetProperty(propertyName, out value) || value.ValueKind != JsonValueKind.String)
            {
                throw new PlatformConfigurationException(methodCode + " requires string parameter " + propertyName + ".");
            }

            string? result = value.GetString();
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new PlatformConfigurationException(methodCode + " requires string parameter " + propertyName + ".");
            }

            return result;
        }

        private static int ParseCanonicalIntradayMinutes(string canonicalName)
        {
            const string suffix = "minute";
            string numericPart = canonicalName.Substring(0, canonicalName.Length - suffix.Length);
            return int.Parse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        private static string BuildRevision(IReadOnlyList<ConfigTimeframe> timeframeRows,
            IReadOnlyList<ConfigCriticalLevel> criticalLevelRows)
        {
            List<string> parts = new List<string>();
            for (int index = 0; index < timeframeRows.Count; index++)
            {
                ConfigTimeframe row = timeframeRows[index];
                parts.Add("T|" + row.TimeframeCode + "|" + row.MinutesMultiplier.ToString(CultureInfo.InvariantCulture)
                    + "|" + row.SourceStream + "|" + row.IsActive.ToString(CultureInfo.InvariantCulture));
            }

            for (int index = 0; index < criticalLevelRows.Count; index++)
            {
                ConfigCriticalLevel row = criticalLevelRows[index];
                parts.Add("C|" + row.MethodCode + "|" + row.AppliedTimeframe + "|" + row.ReferenceTimeframe + "|"
                    + row.ParametersJson + "|" + row.MinimumBarsRequired.ToString(CultureInfo.InvariantCulture) + "|"
                    + row.IsActive.ToString(CultureInfo.InvariantCulture));
            }

            parts.Sort(StringComparer.Ordinal);
            string material = string.Join("\n", parts);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash);
        }
    }
}
