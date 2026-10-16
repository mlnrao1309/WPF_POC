using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class SqlRangeExtensionPivots
    {
        private readonly decimal pivot;
        private readonly decimal resistance1;
        private readonly decimal resistance2;
        private readonly decimal resistance3;
        private readonly decimal resistance4;
        private readonly decimal resistance5;
        private readonly decimal support1;
        private readonly decimal support2;
        private readonly decimal support3;
        private readonly decimal support4;
        private readonly decimal support5;

        public SqlRangeExtensionPivots(decimal pivot, decimal resistance1, decimal resistance2, decimal resistance3, decimal resistance4, decimal resistance5, decimal support1, decimal support2, decimal support3, decimal support4, decimal support5)
        {
            this.pivot = pivot;
            this.resistance1 = resistance1;
            this.resistance2 = resistance2;
            this.resistance3 = resistance3;
            this.resistance4 = resistance4;
            this.resistance5 = resistance5;
            this.support1 = support1;
            this.support2 = support2;
            this.support3 = support3;
            this.support4 = support4;
            this.support5 = support5;
        }

        public decimal Pivot
        {
            get
            {
                return this.pivot;
            }
        }

        public decimal Resistance1
        {
            get
            {
                return this.resistance1;
            }
        }

        public decimal Resistance2
        {
            get
            {
                return this.resistance2;
            }
        }

        public decimal Resistance3
        {
            get
            {
                return this.resistance3;
            }
        }

        public decimal Resistance4
        {
            get
            {
                return this.resistance4;
            }
        }

        public decimal Resistance5
        {
            get
            {
                return this.resistance5;
            }
        }

        public decimal Support1
        {
            get
            {
                return this.support1;
            }
        }

        public decimal Support2
        {
            get
            {
                return this.support2;
            }
        }

        public decimal Support3
        {
            get
            {
                return this.support3;
            }
        }

        public decimal Support4
        {
            get
            {
                return this.support4;
            }
        }

        public decimal Support5
        {
            get
            {
                return this.support5;
            }
        }

        public string MethodVersion
        {
            get
            {
                return "SqlRangeExtensionV1";
            }
        }
    }
}
