using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class IndicatorCalculationCache
    {
        private readonly Dictionary<IndicatorCalculationCacheKey, double[]> values = new Dictionary<IndicatorCalculationCacheKey, double[]>();
        private readonly object sync = new object();

        public int Count
        {
            get
            {
                lock (this.sync)
                {
                    return this.values.Count;
                }
            }
        }

        public bool TryGet(IndicatorCalculationCacheKey key, out double[]? values)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            lock (this.sync)
            {
                double[]? stored;
                if (this.values.TryGetValue(key, out stored))
                {
                    values = (double[])stored.Clone();
                    return true;
                }
                values = null;
                return false;
            }
        }

        public void Set(IndicatorCalculationCacheKey key, double[] values)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            lock (this.sync)
            {
                this.values[key] = (double[])values.Clone();
            }
        }

        public void Clear()
        {
            lock (this.sync)
            {
                this.values.Clear();
            }
        }
    }
}
