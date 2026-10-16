using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class RoundValue : ValueDefinition, IEquatable<RoundValue>
    {
        private ValueDefinition source;
        private int decimalPlaces;

        [JsonConstructor]
        public RoundValue(ValueDefinition Source, int DecimalPlaces = 0)
        {
            this.source = Source;
            this.decimalPlaces = DecimalPlaces;
        }

        [JsonRequired]
        public ValueDefinition Source
        {
            get
            {
                return this.source;
            }
            init
            {
                this.source = value;
            }
        }

        public int DecimalPlaces
        {
            get
            {
                return this.decimalPlaces;
            }
            init
            {
                this.decimalPlaces = value;
            }
        }

        public bool Equals(RoundValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ValueDefinition>.Default.Equals(Source, other.Source)
                && EqualityComparer<int>.Default.Equals(DecimalPlaces, other.DecimalPlaces);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as RoundValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(RoundValue));
            hash.Add(Source);
            hash.Add(DecimalPlaces);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ValueDefinition Source, out int DecimalPlaces)
        {
            Source = this.Source;
            DecimalPlaces = this.DecimalPlaces;
        }
    }
}


