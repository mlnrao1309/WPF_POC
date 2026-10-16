using System;
using System.Collections.Generic;
using System.Text;

namespace AlgoTrading.Models
{
    namespace MarketData.Entities
    {
        public class InstrumentEq
        {
            public string Id { get; set; } = string.Empty;
            public long InstrumentToken { get; set; }
            public string TradingSymbol { get; set; } = string.Empty;
            public string Segment { get; set; } = string.Empty;
            public string Exchange { get; set; } = string.Empty;
            public long LotSize { get; set; }
        }

        public class InstrumentFo
        {
            public string Id { get; set; } = string.Empty;
            public long InstrumentToken { get; set; }
            public string TradingSymbol { get; set; } = string.Empty;
            public string Segment { get; set; } = string.Empty;
            public string Exchange { get; set; } = string.Empty;
            public long? LotSize { get; set; }
            public DateTime Expiry { get; set; }
        }
        public sealed class MarketWatch
        {
            public long Idx { get; set; }                      // IDENTITY (not part of PK)

            public int WatchId { get; set; }                   // PK (1)
            public string Name { get; set; } = string.Empty;   // PK (2)
            public long InstrumentToken { get; set; }          // PK (3)

            public bool IsDeleted { get; set; }
            public bool IsActive { get; set; } = true;

            public DateTime InsertedOn { get; set; }
            public DateTime? DeletedOn { get; set; }
            public DateTime? ActiveInactiveOn { get; set; }
            public DateTime? LastUpdateTime { get; set; }

            public string? TradingSymbol { get; set; }
            public string? Segment { get; set; }               // nchar(10)
            public string? Exchange { get; set; }              // nchar(10)
            public DateTime? ExpiresOn { get; set; }
            public string? FullId { get; set; }
        }

    }
}
