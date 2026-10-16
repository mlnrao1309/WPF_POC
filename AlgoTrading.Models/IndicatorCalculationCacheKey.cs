using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class IndicatorCalculationCacheKey : IEquatable<IndicatorCalculationCacheKey>
    {
        private readonly int instrumentToken;
        private readonly IndicatorDependency dependency;
        private readonly string algorithmVersion;
        private readonly string dataRevision;

        public IndicatorCalculationCacheKey(int instrumentToken, IndicatorDependency dependency,
            string algorithmVersion, string dataRevision)
        {
            if (dependency == null || string.IsNullOrWhiteSpace(algorithmVersion) || string.IsNullOrWhiteSpace(dataRevision))
            {
                throw new ArgumentException("Dependency, algorithm version and data revision are required.");
            }
            this.instrumentToken = instrumentToken;
            this.dependency = dependency;
            this.algorithmVersion = algorithmVersion;
            this.dataRevision = dataRevision;
        }

        public bool Equals(IndicatorCalculationCacheKey? other)
        {
            if (other == null)
            {
                return false;
            }
            return this.instrumentToken == other.instrumentToken && this.dependency.Equals(other.dependency)
                && this.algorithmVersion == other.algorithmVersion && this.dataRevision == other.dataRevision;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as IndicatorCalculationCacheKey);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(this.instrumentToken, this.dependency, this.algorithmVersion, this.dataRevision);
        }
    }
}
