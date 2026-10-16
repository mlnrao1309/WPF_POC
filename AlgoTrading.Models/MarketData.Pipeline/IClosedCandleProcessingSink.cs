using AlgoTrading.Models.MarketData.Processing;

namespace AlgoTrading.Models.MarketData.Pipeline
{
    public interface IClosedCandleProcessingSink
    {
        Task HandleAsync(ClosedCandleProcessingResult result, CancellationToken cancellationToken);
    }
}
