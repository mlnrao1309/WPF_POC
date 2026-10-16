namespace AlgoTrading.Models.MarketData.Ingestion
{
    public interface IHistoricalChunkSource
    {
        Task<string> DownloadChunkAsync(HistoricalChunkRequest request, CancellationToken cancellationToken);
    }
}
