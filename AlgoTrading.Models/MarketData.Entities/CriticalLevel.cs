using System;

namespace AlgoTrading.Models.MarketData.Entities
{
    /// <summary>Persisted critical level produced by one calculator for one instrument and timeframe.</summary>
    public class CriticalLevel
    {
        public long Id { get; set; }

        public long InstrumentToken { get; set; }

        public string TradingSymbol { get; set; } = string.Empty;

        public string Timeframe { get; set; } = string.Empty;

        /// <summary>LevelType codes: PIVOT_PP, PIVOT_R1, EMA_CROSS, SWING_HIGH, SWING_LOW.</summary>
        public string LevelType { get; set; } = string.Empty;

        public decimal Price { get; set; }

        /// <summary>Close time of the candle that produced the level.</summary>
        public DateTime LevelTimestamp { get; set; }

        /// <summary>Instant at which the level became actionable; never earlier than LevelTimestamp.</summary>
        public DateTime ConfirmedAtTimestamp { get; set; }

        public string MethodCode { get; set; } = string.Empty;

        public string? MetaDataJson { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
