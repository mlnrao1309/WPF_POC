using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(ComparisonCondition), "comparison")]
    [JsonDerivedType(typeof(ConditionGroup), "group")]
    [JsonDerivedType(typeof(NotCondition), "not")]
    public abstract class ConditionDefinition
    {
        private bool enabled = true;
        private string? comment;

        public bool Enabled
        {
            get
        {
            return this.enabled;
        }
            init
        {
            this.enabled = value;
        }
        }

        public string? Comment
        {
            get
        {
            return this.comment;
        }
            init
        {
            this.comment = value;
        }
        }

        public abstract override bool Equals(object? other);
        public abstract override int GetHashCode();

        public static bool operator ==(ConditionDefinition? left, ConditionDefinition? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(ConditionDefinition? left, ConditionDefinition? right)
        {
            return !object.Equals(left, right);
        }
    }
}



