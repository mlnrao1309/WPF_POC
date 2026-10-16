using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models.MarketData.Processing
{
    /// <summary>A provider observation. Only explicitly finalized candles can cross the coordinator boundary.</summary>
    public sealed class FinalizedSourceCandleEvent
    {
        public FinalizedSourceCandleEvent(string streamType, CompletedCandle candle, DateTimeOffset observedAt,
            bool providerFinalized)
        {
            this.StreamType = streamType;
            this.Candle = candle;
            this.ObservedAt = observedAt;
            this.ProviderFinalized = providerFinalized;
        }

        public string StreamType { get; }

        public CompletedCandle Candle { get; }

        public DateTimeOffset ObservedAt { get; }

        public bool ProviderFinalized { get; }
    }
}
