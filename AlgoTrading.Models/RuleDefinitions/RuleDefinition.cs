using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class RuleDefinition : IEquatable<RuleDefinition>
    {
        private string name;
        private string evaluationTimeframe;
        private ConditionDefinition condition;

        [JsonConstructor]
        public RuleDefinition(string Name, string EvaluationTimeframe, ConditionDefinition Condition)
        {
            this.name = Name;
            this.evaluationTimeframe = EvaluationTimeframe;
            this.condition = Condition;
        }

        [JsonRequired]
        public string Name
        {
            get
            {
                return this.name;
            }
            init
            {
                this.name = value;
            }
        }

        [JsonRequired]
        public string EvaluationTimeframe
        {
            get
            {
                return this.evaluationTimeframe;
            }
            init
            {
                this.evaluationTimeframe = value;
            }
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

        public bool Equals(RuleDefinition? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<string>.Default.Equals(Name, other.Name)
                && EqualityComparer<string>.Default.Equals(EvaluationTimeframe, other.EvaluationTimeframe)
                && EqualityComparer<ConditionDefinition>.Default.Equals(Condition, other.Condition);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as RuleDefinition);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(RuleDefinition));
            hash.Add(Name);
            hash.Add(EvaluationTimeframe);
            hash.Add(Condition);
            return hash.ToHashCode();
        }

        public static bool operator ==(RuleDefinition? left, RuleDefinition? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(RuleDefinition? left, RuleDefinition? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out string Name, out string EvaluationTimeframe, out ConditionDefinition Condition)
        {
            Name = this.Name;
            EvaluationTimeframe = this.EvaluationTimeframe;
            Condition = this.Condition;
        }
    }
}


