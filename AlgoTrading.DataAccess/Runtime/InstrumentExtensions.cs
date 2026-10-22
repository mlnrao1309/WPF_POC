using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.Runtime;

public static class InstrumentExtensions
{
    public static void AppendLiveCandle(this Instruments instrument, Timeframe timeframe, Candle newCandle)
    {
        lock (instrument)
        {
            var existing = instrument.TryGetCandles(timeframe, out var current)
                ? current.ToList()
                : new List<Candle>();

            // 1. Check if we are updating the live candle
            if (existing.Count > 0 && existing[^1].CloseTime == newCandle.CloseTime)
            {
                existing[^1] = newCandle;
            }
            // 2. Append new candle
            else if (existing.Count == 0 || existing[^1].CloseTime < newCandle.CloseTime)
            {
                existing.Add(newCandle);
            }
            // 3. Handle out-of-order packets
            else
            {
                existing.Add(newCandle);
                existing = existing.OrderBy(c => c.CloseTime).ToList();
            }

            // 4. Push the updated array back into the aggregate root
            instrument.SetCandles(timeframe, existing);
        }
    }
}