using System;

namespace AlgoTrading.Models
{
    public static class TimeframeValidation
    {
        public static void RequireIntraday(TimeframeDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (definition.Kind != TimeframeKind.Intraday || !definition.Duration.HasValue)
            {
                throw new ArgumentException("An intraday timeframe is required.", nameof(definition));
            }
        }

        public static void RequireCalendar(TimeframeDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (definition.Kind == TimeframeKind.Intraday)
            {
                throw new ArgumentException("A daily, weekly or monthly calendar timeframe is required.", nameof(definition));
            }
        }
    }
}
