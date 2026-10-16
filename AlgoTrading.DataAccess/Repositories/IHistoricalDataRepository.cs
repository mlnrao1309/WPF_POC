using AlgoTrading.Models;

namespace AlgoTrading.Repositories;

public interface IHistoricalDataRepository
{
    Task<int> InsertBatchAsync(InstrumentCandleData batch, CancellationToken cancellationToken = default);

    IAsyncEnumerable<InstrumentCandleData> GetPagesAsync(int instrumentToken, string timeframe, int pageSize,
        CancellationToken cancellationToken = default);

    Task<InstrumentCandleData> GetByDateRangeAsync(int instrumentToken, string timeframe,
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken = default);
}
