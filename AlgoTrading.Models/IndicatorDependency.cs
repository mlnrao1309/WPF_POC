using System;
using AlgoTrading.Models.Rules;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;

namespace AlgoTrading.Models
{
    public sealed class IndicatorDependency : IEquatable<IndicatorDependency>
    {
        private readonly IndicatorFunction function;
        private readonly string timeframe;
        private readonly int length;
        private readonly double multiplier;
        private readonly ValueDefinition? source;
        private readonly int period2;
        private readonly int period3;
        private readonly IndicatorComponent component;
        private readonly MovingAverageType? smoothing;

        internal IndicatorDependency(IndicatorValue indicator)
        {
            this.function = indicator.Function;
            this.timeframe = indicator.Timeframe;
            this.length = indicator.Length;
            this.multiplier = indicator.Multiplier;
            this.source = indicator.Source;
            this.period2 = indicator.Period2;
            this.period3 = indicator.Period3;
            this.component = indicator.Component;
            this.smoothing = indicator.Smoothing;
        }

        public IndicatorFunction Function
        {
            get
            {
                return this.function;
            }
        }
        public string Timeframe
        {
            get
            {
                return this.timeframe;
            }
        }
        public int Length
        {
            get
            {
                return this.length;
            }
        }
        public double Multiplier
        {
            get
            {
                return this.multiplier;
            }
        }
        public ValueDefinition? Source
        {
            get
            {
                return this.source;
            }
        }
        public int Period2 => this.period2;
        public int Period3 => this.period3;
        public IndicatorComponent Component => this.component;
        public MovingAverageType? Smoothing => this.smoothing;

        public bool Equals(IndicatorDependency? other)
        {
            if (other == null)
            {
                return false;
            }
            return this.function == other.function && this.timeframe == other.timeframe && this.length == other.length
                && this.multiplier == other.multiplier && Equals(this.source, other.source)
                && this.period2 == other.period2 && this.period3 == other.period3
                && this.component == other.component && this.smoothing == other.smoothing;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as IndicatorDependency);
        }
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(this.function); hash.Add(this.timeframe); hash.Add(this.length); hash.Add(this.multiplier);
            hash.Add(this.source); hash.Add(this.period2); hash.Add(this.period3); hash.Add(this.component); hash.Add(this.smoothing);
            return hash.ToHashCode();
        }
        public static bool operator ==(IndicatorDependency? left, IndicatorDependency? right)
        {
            return object.Equals(left, right);
        }
        public static bool operator !=(IndicatorDependency? left, IndicatorDependency? right)
        {
            return !object.Equals(left, right);
        }
    }
}
