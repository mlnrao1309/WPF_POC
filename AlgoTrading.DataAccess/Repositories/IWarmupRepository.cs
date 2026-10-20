using Stage1Scanner.Core.Market;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlgoTrading.DataAccess.Repositories
{
    public interface IWarmupRepository
    {
        /// <summary>
        /// Fetches instruments with stale or missing daily data based on the expected trading date.
        /// </summary>
        Task<IEnumerable<DisqualifiedInstrumentDto>> GetDisqualifiedInstrumentsAsync();

        /// <summary>
        /// Fetches the multi-result warmup snapshot (Pivots, 60m candles, 15m candles) 
        /// for the specified lookback window.
        /// </summary>
        Task<Dictionary<long, Instruments>> GetWarmupSnapshotAsync(short weeksNeeded);
    }

    // Simple DTO for the disqualified list result set
    public record DisqualifiedInstrumentDto(
        long InstrumentToken,
        string TradingSymbol,
        DateTime LatestTimestamp
    );
}
