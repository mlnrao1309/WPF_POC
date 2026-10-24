using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Aggregation;

/// <summary>
/// Per-backtest-run cache of aggregated bar series. Keyed by Timeframe
/// (one cache per base series/instrument token). Lives for one run.
/// </summary>
public sealed class TimeframeCache
{
    private readonly IBarSeries _base;
    private readonly Dictionary<Timeframe, IBarSeries> _cache;

    public TimeframeCache(IBarSeries baseSeries, IBarSeries? dailySeries = null,
        IReadOnlyDictionary<Timeframe, IBarSeries>? preparedSeries = null)
    {
        _base = baseSeries ?? throw new ArgumentNullException(nameof(baseSeries));
        _cache = new Dictionary<Timeframe, IBarSeries>(capacity: 8)
        {
            [baseSeries.Timeframe] = baseSeries
        };
        if (dailySeries is not null)
            _cache[Timeframe.Parse("D")] = dailySeries;
        if (preparedSeries is not null)
            foreach (var pair in preparedSeries)
            {
                if (pair.Value.InstrumentToken != baseSeries.InstrumentToken)
                    throw new ArgumentException("Prepared candles belong to a different instrument.", nameof(preparedSeries));
                if (!pair.Key.Equals(baseSeries.Timeframe)) _cache[pair.Key] = pair.Value;
            }
    }

    public IBarSeries Get(uint instrumentToken, Timeframe timeframe)
    {
        if (instrumentToken != _base.InstrumentToken)
            throw new InvalidOperationException(
                $"Instrument token {instrumentToken} does not match base series token {_base.InstrumentToken}");
        return Get(timeframe);
    }

    public IBarSeries Get(Timeframe timeframe)
    {
        if (_cache.TryGetValue(timeframe, out IBarSeries? cached) && cached is not null)
            return cached;

        IBarSeries aggregated = TimeframeAggregator.Aggregate(_base, timeframe);
        _cache[timeframe] = aggregated;
        return aggregated;
    }
}
