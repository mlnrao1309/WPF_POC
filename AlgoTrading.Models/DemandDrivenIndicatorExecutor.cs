using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class DemandDrivenIndicatorExecutor
    {
        private readonly IndicatorDependencyPlan plan;
        private readonly IIndicatorSeriesCalculator calculator;
        private readonly IndicatorCalculationCache cache;
        private readonly int instrumentToken;
        private readonly string algorithmVersion;
        private readonly string dataRevision;

        public DemandDrivenIndicatorExecutor(IndicatorDependencyPlan plan, IIndicatorSeriesCalculator calculator,
            IndicatorCalculationCache cache, int instrumentToken, string algorithmVersion, string dataRevision)
        {
            if (plan == null || calculator == null || cache == null || string.IsNullOrWhiteSpace(algorithmVersion) || string.IsNullOrWhiteSpace(dataRevision))
            {
                throw new ArgumentException("Plan, calculator, cache, algorithm version and data revision are required.");
            }
            this.plan = plan;
            this.calculator = calculator;
            this.cache = cache;
            this.instrumentToken = instrumentToken;
            this.algorithmVersion = algorithmVersion;
            this.dataRevision = dataRevision;
        }

        public IReadOnlyDictionary<IndicatorDependency, double[]> Calculate(RuleMarketData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (data.InstrumentToken != this.instrumentToken)
            {
                throw new ArgumentException("Data instrument does not match the executor.", nameof(data));
            }
            Dictionary<IndicatorDependency, double[]> result = new Dictionary<IndicatorDependency, double[]>();
            foreach (IndicatorDependency dependency in this.plan.Indicators)
            {
                IndicatorCalculationCacheKey key = new IndicatorCalculationCacheKey(this.instrumentToken, dependency, this.algorithmVersion, this.dataRevision + ":" + data.Fingerprint);
                double[]? values;
                if (!this.cache.TryGet(key, out values))
                {
                    if (this.calculator is BasicIndicatorSeriesCalculator basic)
                    {
                        values = basic.CalculateWithCache(dependency, data, this.cache, this.dataRevision + ":" + this.algorithmVersion);
                    }
                    else
                    {
                        values = this.calculator.Calculate(dependency, data);
                    }
                    this.cache.Set(key, values);
                }
                if (values == null)
                {
                    throw new InvalidOperationException("The indicator calculator returned no series.");
                }
                result.Add(dependency, (double[])values.Clone());
            }
            return result;
        }
    }
}
