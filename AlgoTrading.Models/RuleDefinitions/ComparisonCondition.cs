using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class ComparisonCondition : ConditionDefinition, IEquatable<ComparisonCondition>
    {
        private ValueDefinition left;
        private ComparisonOperator operation;
        private ValueDefinition right;

        [JsonConstructor]
        public ComparisonCondition(ValueDefinition Left, ComparisonOperator Operator, ValueDefinition Right, bool Enabled = true, string? Comment = null)
        {
            this.Enabled = Enabled;
            this.Comment = Comment;
            this.left = Left;
            this.operation = Operator;
            this.right = Right;
        }

        [JsonRequired]
        public ValueDefinition Left
        {
            get
            {
                return this.left;
            }
            init
            {
                this.left = value;
            }
        }

        [JsonRequired]
        public ComparisonOperator Operator
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
        public ValueDefinition Right
        {
            get
            {
                return this.right;
            }
            init
            {
                this.right = value;
            }
        }

        public bool Equals(ComparisonCondition? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ValueDefinition>.Default.Equals(Left, other.Left)
                && EqualityComparer<ComparisonOperator>.Default.Equals(Operator, other.Operator)
                && EqualityComparer<ValueDefinition>.Default.Equals(Right, other.Right)
                && Enabled == other.Enabled
                && EqualityComparer<string?>.Default.Equals(Comment, other.Comment);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as ComparisonCondition);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(ComparisonCondition));
            hash.Add(Left);
            hash.Add(Operator);
            hash.Add(Right);
            hash.Add(Enabled);
            hash.Add(Comment);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ValueDefinition Left, out ComparisonOperator Operator, out ValueDefinition Right)
        {
            Left = this.Left;
            Operator = this.Operator;
            Right = this.Right;
        }
    }
}




