using System;

namespace AlgoTrading.Models
{
    public sealed class CandleAggregationRequest
    {
        private readonly TimeframeDefinition targetTimeframe;
        private readonly TradingSession session;

        public CandleAggregationRequest(TimeframeDefinition targetTimeframe, TradingSession session)
        {
            TimeframeValidation.RequireIntraday(targetTimeframe);
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            this.targetTimeframe = targetTimeframe;
            this.session = session;
        }

        public TimeframeDefinition TargetTimeframe
        {
            get
            {
                return this.targetTimeframe;
            }
        }

        public TradingSession Session
        {
            get
            {
                return this.session;
            }
        }
    }
}
