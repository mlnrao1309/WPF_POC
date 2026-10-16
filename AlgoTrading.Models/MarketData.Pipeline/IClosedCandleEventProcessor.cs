using AlgoTrading.Models.MarketData.Processing;

namespace AlgoTrading.Models.MarketData.Pipeline
{
    public interface IClosedCandleEventProcessor
    {
        ClosedCandleProcessingResult Process(FinalizedSourceCandleEvent sourceEvent);
    }
}
