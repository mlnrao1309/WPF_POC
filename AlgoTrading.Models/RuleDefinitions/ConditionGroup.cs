using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class ConditionGroup : ConditionDefinition, IEquatable<ConditionGroup>
    {
        private GroupOperator operation;
        private IReadOnlyList<ConditionDefinition> conditions;

        [JsonConstructor]
        public ConditionGroup(GroupOperator Operator, IReadOnlyList<ConditionDefinition> Conditions, bool Enabled = true, string? Comment = null)
        {
            this.Enabled = Enabled;
            this.Comment = Comment;
            this.operation = Operator;
            this.conditions = Conditions;
        }

        [JsonRequired]
        public GroupOperator Operator
        {
            get
            {
                return this.operation;
            }
            init
            {
                this.operation = value;
            }
        }

        [JsonRequired]
        public IReadOnlyList<ConditionDefinition> Conditions
        {
            get
            {
                return this.conditions;
            }
            init
            {
                this.conditions = value;
            }
        }

        public bool Equals(ConditionGroup? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<GroupOperator>.Default.Equals(Operator, other.Operator)
                && EqualityComparer<IReadOnlyList<ConditionDefinition>>.Default.Equals(Conditions, other.Conditions)
                && Enabled == other.Enabled
                && EqualityComparer<string?>.Default.Equals(Comment, other.Comment);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as ConditionGroup);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(ConditionGroup));
            hash.Add(Operator);
            hash.Add(Conditions);
            hash.Add(Enabled);
            hash.Add(Comment);
            return hash.ToHashCode();
        }

        public void Deconstruct(out GroupOperator Operator, out IReadOnlyList<ConditionDefinition> Conditions)
        {
            Operator = this.Operator;
            Conditions = this.Conditions;
        }
    }
}




