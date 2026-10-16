using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class DailyPivotResult
    {
        private readonly CompletedCandle source;
        private readonly TradingSession effectiveSession;
        private readonly SqlRangeExtensionPivots pivots;
        private readonly bool? ppr1;
        private readonly bool? pps1;
        private readonly CriticalLevelOrigin? origin;

        internal DailyPivotResult(CompletedCandle source, TradingSession effectiveSession, SqlRangeExtensionPivots pivots, bool? ppr1, bool? pps1, CriticalLevelOrigin? origin)
        {
            this.source = source;
            this.effectiveSession = effectiveSession;
            this.pivots = pivots;
            this.ppr1 = ppr1;
            this.pps1 = pps1;
            this.origin = origin;
        }

        public CompletedCandle Source
        {
            get
            {
                return this.source;
            }
        }

        public TradingSession EffectiveSession
        {
            get
            {
                return this.effectiveSession;
            }
        }

        public SqlRangeExtensionPivots Pivots
        {
            get
            {
                return this.pivots;
            }
        }

        public bool? Ppr1
        {
            get
            {
                return this.ppr1;
            }
        }

        public bool? Pps1
        {
            get
            {
                return this.pps1;
            }
        }

        public CriticalLevelOrigin? Origin
        {
            get
            {
                return this.origin;
            }
        }
    }
}
