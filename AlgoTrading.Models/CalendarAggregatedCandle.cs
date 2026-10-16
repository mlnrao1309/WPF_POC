using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CalendarAggregatedCandle
    {
        private readonly CompletedCandle aggregate;
        private readonly CompletedCandle firstConstituent;
        private readonly CompletedCandle lastConstituent;
        private readonly DateOnly periodStart;
        private readonly DateOnly periodEnd;
        private readonly TimeframeDefinition timeframe;

        internal CalendarAggregatedCandle(CompletedCandle aggregate, CompletedCandle firstConstituent,
            CompletedCandle lastConstituent, DateOnly periodStart, DateOnly periodEnd, TimeframeDefinition timeframe)
        {
            this.aggregate = aggregate;
            this.firstConstituent = firstConstituent;
            this.lastConstituent = lastConstituent;
            this.periodStart = periodStart;
            this.periodEnd = periodEnd;
            this.timeframe = timeframe;
        }

        public CompletedCandle Aggregate
        {
            get
            {
                return this.aggregate;
            }
        }

        public CompletedCandle FirstConstituent
        {
            get
            {
                return this.firstConstituent;
            }
        }

        public CompletedCandle LastConstituent
        {
            get
            {
                return this.lastConstituent;
            }
        }

        public DateOnly PeriodStart
        {
            get
            {
                return this.periodStart;
            }
        }

        public DateOnly PeriodEnd
        {
            get
            {
                return this.periodEnd;
            }
        }

        public TimeframeDefinition Timeframe
        {
            get
            {
                return this.timeframe;
            }
        }
    }
}
