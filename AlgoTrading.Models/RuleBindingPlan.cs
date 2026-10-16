using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlgoTrading.Models.Rules
{
    public sealed class RuleBindingPlan : IEquatable<RuleBindingPlan>
    {
        private IReadOnlyList<string> requiredTimeframes;
        private IReadOnlyList<string> indicatorCalculations;

        public RuleBindingPlan(IReadOnlyList<string> RequiredTimeframes, IReadOnlyList<string> IndicatorCalculations)
        {
            requiredTimeframes = RequiredTimeframes;
            indicatorCalculations = IndicatorCalculations;
        }

        public IReadOnlyList<string> RequiredTimeframes
        {
            get
            {
                return requiredTimeframes;
            }
            init
            {
                requiredTimeframes = value;
            }
        }

        public IReadOnlyList<string> IndicatorCalculations
        {
            get
            {
                return indicatorCalculations;
            }
            init
            {
                indicatorCalculations = value;
            }
        }

        public bool Equals(RuleBindingPlan? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<IReadOnlyList<string>>.Default.Equals(RequiredTimeframes, other.RequiredTimeframes)
                && EqualityComparer<IReadOnlyList<string>>.Default.Equals(IndicatorCalculations, other.IndicatorCalculations);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as RuleBindingPlan);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(typeof(RuleBindingPlan), RequiredTimeframes, IndicatorCalculations);
        }

        public static bool operator ==(RuleBindingPlan? left, RuleBindingPlan? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(RuleBindingPlan? left, RuleBindingPlan? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out IReadOnlyList<string> RequiredTimeframes, out IReadOnlyList<string> IndicatorCalculations)
        {
            RequiredTimeframes = this.RequiredTimeframes;
            IndicatorCalculations = this.IndicatorCalculations;
        }
    }
}
