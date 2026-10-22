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

            var timeframe = ParseTimeframe(tick.Timeframe);

            // 1. Update the immutable Candle array in memory (Copy-On-Write)
            instrument.AppendLiveCandle(timeframe, tick.NewCandle);

            // 2. If the candle has CLOSED, recalculate the indicator tails
            if (tick.IsCandleClosed)
            {
                RecalculateIndicators(instrument, tick.Timeframe, timeframe);
            }
        }
    }

    private void RecalculateIndicators(Instruments inst, string tfStr, Timeframe timeframe)
    {
        // NOTE: In a real scenario, you only need to recalculate the LAST 1 or 2 bars 
        // for EMA/RSI/SuperTrend. For this example, we will assume the Core library 
        // has a method that calculates the whole series (which is fast in memory).
        
        if (!inst.TryGetCandles(timeframe, out var candles)) return;

        // Loop through the indicators that the ETL pre-calculated for this timeframe
        // (In a production system, you'd cache a list of "ActiveIndicators" to avoid recalculating things no scanner uses).
        
        // --- Example: Recalculate EMA_5 ---
        /*
        var newEma = CoreMath.CalculateEma(candles, 5);
        var outputs = new Dictionary<string, List<decimal?>>
        {
            { "Value", newEma }
        };
        _registry.UpdateLiveIndicatorSeries(inst.InstrumentToken, tfStr, "EMA", 5, outputs);
        */

        // --- Example: Recalculate SUPERTREND_10 ---
        /*
        var st = CoreMath.CalculateSuperTrend(candles, 10);
        var outputs = new Dictionary<string, List<decimal?>>
        {
            { "Value", st.ValueLine },
            { "Direction", st.DirectionLine }
        };
        _registry.UpdateLiveIndicatorSeries(inst.InstrumentToken, tfStr, "SUPERTREND", 10, outputs);
        */
    }

    private Timeframe ParseTimeframe(string tf)
    {
        return tf.ToUpper() switch
        {
            "D" => new Timeframe(TimeframeUnit.Day, 1),
            "W" => new Timeframe(TimeframeUnit.Week, 1),
            _   => new Timeframe(TimeframeUnit.Minute, int.Parse(tf))
        };
    }
}