using System;

namespace AlgoTrading.Models.MarketData.Entities
{
    /// <summary>Resumption state for one instrument and stream so a stopped backfill never re-downloads a chunk.</summary>
    public class IngestionSyncState
    {
        public long Id { get; set; }

        public long InstrumentToken { get; set; }

        /// <summary>StreamType codes: INTRADAY_5M and DAILY_1D.</summary>
        public string StreamType { get; set; } = string.Empty;

        public DateTime LastCompletedChunkStartDate { get; set; }

        public DateTime LastCompletedChunkEndDate { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }
}
