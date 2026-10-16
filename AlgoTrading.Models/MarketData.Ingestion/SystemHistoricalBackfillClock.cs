namespace AlgoTrading.Models.MarketData.Ingestion
{
    public sealed class SystemHistoricalBackfillClock : IHistoricalBackfillClock
    {
        public DateTime UtcNow
        {
            get
            {
                return DateTime.UtcNow;
            }
        }
    }
}
