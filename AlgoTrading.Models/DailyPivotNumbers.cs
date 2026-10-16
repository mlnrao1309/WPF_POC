using System;

namespace AlgoTrading.Models
{
    public sealed class DailyPivotNumbers
    {
        private readonly SqlRangeExtensionPivots pivots;
        private readonly bool ppr1;
        private readonly bool pps1;

        public DailyPivotNumbers(SqlRangeExtensionPivots pivots, bool ppr1, bool pps1)
        {
            ArgumentNullException.ThrowIfNull(pivots);
            this.pivots = pivots;
            this.ppr1 = ppr1;
            this.pps1 = pps1;
        }

        public SqlRangeExtensionPivots Pivots
        {
            get
            {
                return this.pivots;
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
    }
}
