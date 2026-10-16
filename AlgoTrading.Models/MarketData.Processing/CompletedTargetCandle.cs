using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models.MarketData.Processing
{
    /// <summary>A target candle emitted once with a stable replay identity and calendar provenance.</summary>
    public sealed class CompletedTargetCandle
    {
        public CompletedTargetCandle(string identity, string timeframeCode, CompletedCandle candle,
            string calendarRevision)
        {
            this.Identity = identity;
            this.TimeframeCode = timeframeCode;
            this.Candle = candle;
            this.CalendarRevision = calendarRevision;
        }

        public string Identity { get; }

        public string TimeframeCode { get; }

        public CompletedCandle Candle { get; }

        public string CalendarRevision { get; }
    }
}
