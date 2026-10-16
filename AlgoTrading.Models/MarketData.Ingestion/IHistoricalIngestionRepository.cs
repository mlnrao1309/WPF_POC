namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>
    /// Durable checkpoint boundary. Implementations must write the candle chunk idempotently and advance the
    /// checkpoint in the same transaction. A failed call must leave both unchanged.
    /// </summary>
    public interface IHistoricalIngestionRepository
    {
        Task<HistoricalIngestionCheckpoint?> GetCheckpointAsync(long instrumentToken, string streamType,
            CancellationToken cancellationToken);

        Task PersistChunkAndCheckpointAsync(HistoricalChunkRequest request, string providerPayload,
            CancellationToken cancellationToken);
    }
}
