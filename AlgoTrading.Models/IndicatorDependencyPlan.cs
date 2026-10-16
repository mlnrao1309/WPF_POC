using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class IndicatorDependencyPlan
    {
        private readonly IReadOnlyList<string> requiredTimeframes;
        private readonly IReadOnlyList<IndicatorDependency> indicators;

        internal IndicatorDependencyPlan(IReadOnlyList<string> requiredTimeframes, IReadOnlyList<IndicatorDependency> indicators)
        {
            this.requiredTimeframes = requiredTimeframes;
            this.indicators = indicators;
        }

        public IReadOnlyList<string> RequiredTimeframes
        {
            get
            {
                return this.requiredTimeframes;
            }
        }
        public IReadOnlyList<IndicatorDependency> Indicators
        {
            get
            {
                return this.indicators;
            }
        }
    }
}
