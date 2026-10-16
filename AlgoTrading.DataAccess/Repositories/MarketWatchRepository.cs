using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Repositories;
using Microsoft.EntityFrameworkCore;


namespace AlgoTrading.Repositories;
public class MarketWatchRepository : IMarketWatchRepository
{
    private readonly ApplicationDbContext _context;

    public MarketWatchRepository(ApplicationDbContext context) => _context = context;

    // ---------------------------------------------------------------
    // (1) All non-deleted. The global query filter already excludes
    //     IsDeleted = true, but we keep it explicit for readability.
    // ---------------------------------------------------------------
    public async Task<IReadOnlyList<MarketWatch>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.MarketWatches
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------
    // (2) Filter by segment and/or exchange
    // ---------------------------------------------------------------
    public async Task<IReadOnlyList<MarketWatch>> GetBySegmentAndExchangeAsync(
        string? segment = null, string? exchange = null, CancellationToken ct = default)
    {
        IQueryable<MarketWatch> q = _context.MarketWatches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(segment))
            q = q.Where(x => x.Segment == segment);

        if (!string.IsNullOrWhiteSpace(exchange))
            q = q.Where(x => x.Exchange == exchange);

        return await q.ToListAsync(ct);
    }

    // ---------------------------------------------------------------
    // (3) Grouped by {WatchId, Name}
    // ---------------------------------------------------------------
    public async Task<IReadOnlyList<MarketWatchGroup>> GetGroupedByWatchAsync(CancellationToken ct = default)
    {
        var rows = await _context.MarketWatches
            .AsNoTracking()
            .OrderBy(x => x.WatchId).ThenBy(x => x.Name)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.WatchId, x.Name })
            .Select(g => new MarketWatchGroup(g.Key.WatchId, g.Key.Name, g.ToList()))
            .ToList();
    }

    // ---------------------------------------------------------------
    // (4) Batch insert, skipping records whose composite key exists
    //     (including soft-deleted, since PK forbids duplicates).
    // ---------------------------------------------------------------
    public async Task<int> InsertBatchIfNotExistsAsync(
        IEnumerable<MarketWatch> items, CancellationToken ct = default)
    {
        var list = items?.ToList() ?? new List<MarketWatch>();
        if (list.Count == 0) return 0;

        // Query existing keys (ignore the soft-delete filter)
        var watchIds = list.Select(x => x.WatchId).Distinct().ToList();
        var names    = list.Select(x => x.Name).Distinct().ToList();
        var tokens   = list.Select(x => x.InstrumentToken).Distinct().ToList();

        var existing = await _context.MarketWatches
            .IgnoreQueryFilters()
            .Where(x => watchIds.Contains(x.WatchId)
                     && names.Contains(x.Name)
                     && tokens.Contains(x.InstrumentToken))
            .Select(x => new { x.WatchId, x.Name, x.InstrumentToken })
            .ToListAsync(ct);

        var existingSet = existing
            .Select(x => (x.WatchId, x.Name, x.InstrumentToken))
            .ToHashSet();

        var toInsert = list
            .Where(x => !existingSet.Contains((x.WatchId, x.Name, x.InstrumentToken)))
            .Select(x =>
            {
                x.InsertedOn = x.InsertedOn == default ? DateTime.UtcNow : x.InsertedOn;
                x.IsDeleted = false;
                return x;
            })
            .ToList();

        if (toInsert.Count == 0) return 0;

        await _context.MarketWatches.AddRangeAsync(toInsert, ct);
        return await _context.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------
    // (5) Soft delete triggered by user
    // ---------------------------------------------------------------
    public async Task<bool> SoftDeleteAsync(
        int watchId, string name, long instrumentToken, CancellationToken ct = default)
    {
        var entity = await _context.MarketWatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.WatchId == watchId
                                   && x.Name == name
                                   && x.InstrumentToken == instrumentToken, ct);

        if (entity is null) return false;
        if (entity.IsDeleted) return true; // already deleted

        var now = DateTime.UtcNow;
        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedOn = now;
        entity.ActiveInactiveOn = now;
        entity.LastUpdateTime = now;

        return await _context.SaveChangesAsync(ct) > 0;
    }

    // ---------------------------------------------------------------
    // (6) Automatic soft-delete of rows whose ExpiresOn has passed.
    //     Called from a background service (see below).
    // ---------------------------------------------------------------
    public async Task<int> PurgeExpiredAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var expired = await _context.MarketWatches
            .IgnoreQueryFilters()
            .Where(x => !x.IsDeleted
                     && x.ExpiresOn != null
                     && x.ExpiresOn <= utcNow)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        foreach (var e in expired)
        {
            e.IsDeleted = true;
            e.IsActive = false;
            e.DeletedOn = utcNow;
            e.ActiveInactiveOn = utcNow;
            e.LastUpdateTime = utcNow;
        }

        return await _context.SaveChangesAsync(ct);
    }
}