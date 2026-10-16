using System;

namespace AlgoTrading.Models.MarketData.Entities
{
    /// <summary>Config_Timeframes row. Disabling a row halts that timeframe without a code deployment.</summary>
    public class ConfigTimeframe
    {
        public int Id { get; set; }

        /// <summary>TimeframeCode: 5m, 15m, 25m, 60m, 75m, 1D, 1W, 1M.</summary>
        public string TimeframeCode { get; set; } = string.Empty;

        public int MinutesMultiplier { get; set; }

        /// <summary>SourceStream codes: INTRADAY_5M and DAILY_1D.</summary>
        public string SourceStream { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    /// <summary>Config_CriticalLevels row: which calculator runs on which timeframe with which parameters.</summary>
    public class ConfigCriticalLevel
    {
        public int Id { get; set; }

        /// <summary>MethodCode: PIVOT_STANDARD, EMA_CROSSOVER, SWING_REVERSAL.</summary>
        public string MethodCode { get; set; } = string.Empty;

        public string AppliedTimeframe { get; set; } = string.Empty;

        public string? ReferenceTimeframe { get; set; }

        public string ParametersJson { get; set; } = "{}";

        public int MinimumBarsRequired { get; set; } = 1;

        public bool IsActive { get; set; } = true;
    }
}
