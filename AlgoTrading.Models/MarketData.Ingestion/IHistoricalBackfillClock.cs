namespace AlgoTrading.Models.MarketData.Ingestion
{
    public interface IHistoricalBackfillClock
    {
        DateTime UtcNow { get; }
    }
}
