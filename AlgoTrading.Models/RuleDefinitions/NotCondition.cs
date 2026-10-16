using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class NotCondition : ConditionDefinition, IEquatable<NotCondition>
    {
        private ConditionDefinition condition;

        [JsonConstructor]
        public NotCondition(ConditionDefinition Condition, bool Enabled = true, string? Comment = null)
        {
            this.Enabled = Enabled;
            this.Comment = Comment;
            this.condition = Condition;
        }

        [JsonRequired]
        public ConditionDefinition Condition
        {
            get
            {
                return this.condition;
            }
            init
            {
                this.condition = value;
            }
        }

        public bool Equals(NotCondition? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ConditionDefinition>.Default.Equals(Condition, other.Condition)
                && Enabled == other.Enabled
                && EqualityComparer<string?>.Default.Equals(Comment, other.Comment);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as NotCondition);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(NotCondition));
            hash.Add(Condition);
            hash.Add(Enabled);
            hash.Add(Comment);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ConditionDefinition Condition)
        {
            Condition = this.Condition;
        }
    }
}



