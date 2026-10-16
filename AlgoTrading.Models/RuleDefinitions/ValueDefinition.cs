using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(NumberValue), "number")]
    [JsonDerivedType(typeof(CandleValue), "candle")]
    [JsonDerivedType(typeof(IndicatorValue), "indicator")]
    [JsonDerivedType(typeof(ArithmeticValue), "arithmetic")]
    [JsonDerivedType(typeof(RoundValue), "round")]
    [JsonDerivedType(typeof(PreviousValue), "previous")]
    [JsonDerivedType(typeof(CountValue), "count")]
    public abstract class ValueDefinition
    {
        public abstract override bool Equals(object? other);
        public abstract override int GetHashCode();

        public static bool operator ==(ValueDefinition? left, ValueDefinition? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(ValueDefinition? left, ValueDefinition? right)
        {
            return !object.Equals(left, right);
        }
    }
}


