using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CriticalLevelOrigin
    {
        private readonly string identity;
        private readonly CompletedCandle source;
        private readonly CompletedCandle qualification;
        private readonly bool ppr1;
        private readonly bool pps1;
        private readonly DateTimeOffset activeFrom;

        internal CriticalLevelOrigin(string identity, CompletedCandle source, CompletedCandle qualification, bool ppr1, bool pps1, DateTimeOffset activeFrom)
        {
            this.identity = identity;
            this.source = source;
            this.qualification = qualification;
            this.ppr1 = ppr1;
            this.pps1 = pps1;
            this.activeFrom = activeFrom;
        }

        public string Identity
        {
            get
            {
                return this.identity;
            }
        }

        public CompletedCandle Source
        {
            get
            {
                return this.source;
            }
        }

        public CompletedCandle Qualification
        {
            get
            {
                return this.qualification;
            }
        }

        public bool Ppr1
        {
            get
            {
                return this.ppr1;
            }
        }

        public bool Pps1
        {
            get
            {
                return this.pps1;
            }
        }

        public DateTimeOffset ActiveFrom
        {
            get
            {
                return this.activeFrom;
            }
        }

        public decimal Resistance
        {
            get
            {
                return this.source.Candle.High;
            }
        }

        public decimal Support
        {
            get
            {
                return this.source.Candle.Low;
            }
        }

        public bool IsActiveAt(DateTimeOffset asOf)
        {
            return asOf >= this.activeFrom;
        }
    }
}
