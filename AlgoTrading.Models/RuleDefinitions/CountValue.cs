using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class CountValue : ValueDefinition, IEquatable<CountValue>
    {
        private ConditionDefinition condition;
        private string timeframe;
        private int length;
        private int candlesAgo;

        [JsonConstructor]
        public CountValue(ConditionDefinition Condition, string Timeframe, int Length, int CandlesAgo = 0)
        {
            this.condition = Condition;
            this.timeframe = Timeframe;
            this.length = Length;
            this.candlesAgo = CandlesAgo;
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

        [JsonRequired]
        public string Timeframe
        {
            get
            {
                return this.timeframe;
            }
            init
            {
                this.timeframe = value;
            }
        }

        [JsonRequired]
        public int Length
        {
            get
            {
                return this.length;
            }
            init
            {
                this.length = value;
            }
        }

        public int CandlesAgo
        {
            get
            {
                return this.candlesAgo;
            }
            init
            {
                this.candlesAgo = value;
            }
        }

        public bool Equals(CountValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ConditionDefinition>.Default.Equals(Condition, other.Condition)
                && EqualityComparer<string>.Default.Equals(Timeframe, other.Timeframe)
                && EqualityComparer<int>.Default.Equals(Length, other.Length)
                && EqualityComparer<int>.Default.Equals(CandlesAgo, other.CandlesAgo);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CountValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CountValue));
            hash.Add(Condition);
            hash.Add(Timeframe);
            hash.Add(Length);
            hash.Add(CandlesAgo);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ConditionDefinition Condition, out string Timeframe, out int Length, out int CandlesAgo)
        {
            Condition = this.Condition;
            Timeframe = this.Timeframe;
            Length = this.Length;
            CandlesAgo = this.CandlesAgo;
        }
    }
}


