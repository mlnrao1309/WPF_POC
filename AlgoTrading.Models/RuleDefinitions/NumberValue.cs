using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class NumberValue : ValueDefinition, IEquatable<NumberValue>
    {
        private double value;

        [JsonConstructor]
        public NumberValue(double Value)
        {
            this.value = Value;
        }

        [JsonRequired]
        public double Value
        {
            get
            {
                return this.value;
            }
            init
            {
                this.value = value;
            }
        }

        public bool Equals(NumberValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<double>.Default.Equals(Value, other.Value);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as NumberValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(NumberValue));
            hash.Add(Value);
            return hash.ToHashCode();
        }

        public void Deconstruct(out double Value)
        {
            Value = this.Value;
        }
    }
}


