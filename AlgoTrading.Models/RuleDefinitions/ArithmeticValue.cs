using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class ArithmeticValue : ValueDefinition, IEquatable<ArithmeticValue>
    {
        private ValueDefinition left;
        private ArithmeticOperator operation;
        private ValueDefinition right;

        [JsonConstructor]
        public ArithmeticValue(ValueDefinition Left, ArithmeticOperator Operator, ValueDefinition Right)
        {
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
        public ArithmeticOperator Operator
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

        public bool Equals(ArithmeticValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ValueDefinition>.Default.Equals(Left, other.Left)
                && EqualityComparer<ArithmeticOperator>.Default.Equals(Operator, other.Operator)
                && EqualityComparer<ValueDefinition>.Default.Equals(Right, other.Right);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as ArithmeticValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(ArithmeticValue));
            hash.Add(Left);
            hash.Add(Operator);
            hash.Add(Right);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ValueDefinition Left, out ArithmeticOperator Operator, out ValueDefinition Right)
        {
            Left = this.Left;
            Operator = this.Operator;
            Right = this.Right;
        }
    }
}



