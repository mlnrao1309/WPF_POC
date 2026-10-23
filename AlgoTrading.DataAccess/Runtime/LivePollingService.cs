using AlgoTrading.DataAccess.Repositories;
using Microsoft.Extensions.Hosting;
using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.Runtime;

public class LivePollingService : BackgroundService
{
    private readonly IInstrumentRegistry _registry;
    private readonly ILiveMarketDataProvider _marketDataProvider;

    public LivePollingService(
        IInstrumentRegistry registry, 
        ILiveMarketDataProvider marketDataProvider)
    {
        _registry = registry;
        _marketDataProvider = marketDataProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        System.Diagnostics.Debug.WriteLine("[LivePolling] Started.");

        await foreach (var tick in _marketDataProvider.StreamLiveCandlesAsync(stoppingToken))
        {
            if (stoppingToken.IsCancellationRequested) break;

            if (tick.InstrumentToken == 0) continue;

            var instrument = _registry.GetByToken(tick.InstrumentToken);
            if (instrument == null) continue; // We don't track this instrument

            if (!tick.IsCandleClosed) continue;
            var timeframe = AlgoTrading.DataAccess.Etl.WarmupEtlService.ParseTimeframe(tick.Timeframe);

            // 1. Update the immutable Candle array in memory (Copy-On-Write)
            instrument.AppendLiveCandle(timeframe, tick.NewCandle);

            // Registry reads calculate indicators from the current immutable candle collection.
        }
    }
}
