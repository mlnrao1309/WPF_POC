using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class RuleEvaluation : IEquatable<RuleEvaluation>
    {
        private RuleStatus status;
        private DateTimeOffset? evaluatedAt;
        private string explanation;

        [JsonConstructor]
        public RuleEvaluation(RuleStatus Status, DateTimeOffset? EvaluatedAt, string Explanation)
        {
            this.status = Status;
            this.evaluatedAt = EvaluatedAt;
            this.explanation = Explanation;
        }

        public RuleStatus Status
        {
            get
            {
                return this.status;
            }
            init
            {
                this.status = value;
            }
        }

        public DateTimeOffset? EvaluatedAt
        {
            get
            {
                return this.evaluatedAt;
            }
            init
            {
                this.evaluatedAt = value;
            }
        }

        public string Explanation
        {
            get
            {
                return this.explanation;
            }
            init
            {
                this.explanation = value;
            }
        }

        public bool IsMatch
        {
            get
            {
                return Status == RuleStatus.Matched;
            }
        }

        public bool Equals(RuleEvaluation? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<RuleStatus>.Default.Equals(Status, other.Status)
                && EqualityComparer<DateTimeOffset?>.Default.Equals(EvaluatedAt, other.EvaluatedAt)
                && EqualityComparer<string>.Default.Equals(Explanation, other.Explanation);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as RuleEvaluation);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(RuleEvaluation));
            hash.Add(Status);
            hash.Add(EvaluatedAt);
            hash.Add(Explanation);
            return hash.ToHashCode();
        }

        public static bool operator ==(RuleEvaluation? left, RuleEvaluation? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(RuleEvaluation? left, RuleEvaluation? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out RuleStatus Status, out DateTimeOffset? EvaluatedAt, out string Explanation)
        {
            Status = this.Status;
            EvaluatedAt = this.EvaluatedAt;
            Explanation = this.Explanation;
        }
    }
}


