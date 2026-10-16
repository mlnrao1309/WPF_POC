using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CriticalReferenceProjection
    {
        private readonly CriticalLevelOrigin origin;
        private readonly TimeframeDefinition timeframe;
        private readonly CompletedCandle start;
        private readonly CompletedCandle end;

        internal CriticalReferenceProjection(CriticalLevelOrigin origin, TimeframeDefinition timeframe, CompletedCandle start, CompletedCandle end)
        {
            this.origin = origin;
            this.timeframe = timeframe;
            this.start = start;
            this.end = end;
        }

        public CriticalLevelOrigin Origin
        {
            get
            {
                return this.origin;
            }
        }

        public TimeframeDefinition Timeframe
        {
            get
            {
                return this.timeframe;
            }
        }

        public CompletedCandle Start
        {
            get
            {
                return this.start;
            }
        }

        public CompletedCandle End
        {
            get
            {
                return this.end;
            }
        }
    }
}
