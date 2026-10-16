using AlgoTrading.Models.MarketData.Processing;

namespace AlgoTrading.Models.MarketData.Pipeline
{
    internal sealed class QueuedFinalizedCandle
    {
        internal QueuedFinalizedCandle(string identity, FinalizedSourceCandleEvent sourceEvent)
        {
            this.Identity = identity;
            this.SourceEvent = sourceEvent;
        }

        internal string Identity { get; }

        internal FinalizedSourceCandleEvent SourceEvent { get; }
    }
}
