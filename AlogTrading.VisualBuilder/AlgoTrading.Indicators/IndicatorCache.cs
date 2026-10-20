using System;
using System.Collections.Concurrent;

namespace AlgoTrading.Indicators;

/// <summary>
/// Thread-safe cache for indicator output series. One instance lives for the
/// duration of a single backtest run (or parameter-sweep combo). Not persisted.
/// </summary>
public sealed class IndicatorCache
{
    private readonly ConcurrentDictionary<IndicatorCacheKey, decimal?[]> _map = new();

    public decimal?[] GetOrAdd(IndicatorCacheKey key, Func<IndicatorCacheKey, decimal?[]> factory)
        => _map.GetOrAdd(key, factory);

    public bool TryGet(IndicatorCacheKey key, out decimal?[] values)
        => _map.TryGetValue(key, out values!);

    public void Clear() => _map.Clear();

    public int Count => _map.Count;
}