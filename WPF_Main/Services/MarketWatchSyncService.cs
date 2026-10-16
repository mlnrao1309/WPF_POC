using AlgoTrading.Dtos;
using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Repositories;
using System.Diagnostics;

namespace AlgoTrading.Services;

public interface IMarketWatchSyncService
{
    event Action<int>? MarketWatchChanged;
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
    Task SynchronizeAsync(MarketWatchApiData marketWatch, CancellationToken cancellationToken = default);
}

public sealed class MarketWatchSyncService : IMarketWatchSyncService
{
    private readonly IMarketWatchApiService _apiService;
    private readonly IMarketWatchRepository _marketWatchRepository;
    private readonly IInstrumentRepository _instrumentRepository;

    public event Action<int>? MarketWatchChanged;

    public MarketWatchSyncService(
        IMarketWatchApiService apiService,
        IMarketWatchRepository marketWatchRepository,
        IInstrumentRepository instrumentRepository)
    {
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        _marketWatchRepository = marketWatchRepository ?? throw new ArgumentNullException(nameof(marketWatchRepository));
        _instrumentRepository = instrumentRepository ?? throw new ArgumentNullException(nameof(instrumentRepository));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        Trace.TraceInformation("MarketWatch synchronization started.");
        IReadOnlyList<MarketWatchApiData> remoteWatches = await _apiService.GetAllMarketWatchesAsync(cancellationToken);
        foreach (MarketWatchApiData remote in remoteWatches)
            await SynchronizeAsync(remote, cancellationToken);
        Trace.TraceInformation("MarketWatch synchronization completed.");
    }

    public async Task SynchronizeAsync(MarketWatchApiData remote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        IReadOnlyList<MarketWatch> local = await _marketWatchRepository.GetByWatchIdAsync(remote.Id, cancellationToken);
        HashSet<long> remoteTokens = remote.Items.Select(static item => item.InstrumentToken).ToHashSet();
        var localKeys = local.Select(static row => (row.Name, row.InstrumentToken)).ToHashSet();
        List<MarketWatch> inserts = [];
        foreach (long token in remoteTokens)
        {
            if (localKeys.Contains((remote.Name, token))) continue;
            if (!await _instrumentRepository.ExistsAsync(token, cancellationToken))
            {
                Trace.TraceError("InstrumentToken {0} was returned remotely but does not exist in Instruments.", token);
                continue;
            }
            inserts.Add(new MarketWatch
            {
                WatchId = remote.Id,
                Name = remote.Name,
                InstrumentToken = token,
                IsDeleted = false,
                IsActive = false,
                InsertedOn = DateTime.UtcNow
            });
        }

        List<MarketWatch> deletes = local
            .Where(row => row.Name != remote.Name || !remoteTokens.Contains(row.InstrumentToken))
            .ToList();
        await _marketWatchRepository.ApplyChangesAsync(inserts, deletes, cancellationToken);
        int unchanged = local.Count(row => row.Name == remote.Name && remoteTokens.Contains(row.InstrumentToken));
        Trace.TraceInformation(
            "WatchId: {0}; Name: {1}; Remote instruments: {2}; Inserted: {3}; Soft deleted: {4}; Unchanged: {5}.",
            remote.Id, remote.Name, remoteTokens.Count, inserts.Count, deletes.Count, unchanged);
        MarketWatchChanged?.Invoke(remote.Id);
    }
}
