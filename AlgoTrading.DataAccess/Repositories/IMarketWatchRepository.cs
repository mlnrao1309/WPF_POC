using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlgoTrading.Repositories;

public record MarketWatchGroup(int WatchId, string Name, IReadOnlyList<MarketWatch> Instruments);

public interface IMarketWatchRepository
{
    // (1) All non-deleted
    Task<IReadOnlyList<MarketWatch>> GetAllActiveAsync(CancellationToken ct = default);

    // (2) By segment and/or exchange
    Task<IReadOnlyList<MarketWatch>> GetBySegmentAndExchangeAsync(
        string? segment = null, string? exchange = null, CancellationToken ct = default);

    // (3) Grouped by {WatchId, Name}
    Task<IReadOnlyList<MarketWatchGroup>> GetGroupedByWatchAsync(CancellationToken ct = default);

    // (4) Batch insert (skips existing key combos)
    Task<int> InsertBatchIfNotExistsAsync(IEnumerable<MarketWatch> items, CancellationToken ct = default);

    // (5) Soft delete (user action)
    Task<bool> SoftDeleteAsync(int watchId, string name, long instrumentToken, CancellationToken ct = default);

    // (6) Automatic soft-delete of expired rows (background)
    Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken ct = default);
}