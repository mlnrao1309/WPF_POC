namespace AlgoTrading.Models.MarketData.Pipeline
{
    public interface IInstrumentCandleProcessorFactory
    {
        IClosedCandleEventProcessor Create(int instrumentToken);
    }
}
