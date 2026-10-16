using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class PreviousValue : ValueDefinition, IEquatable<PreviousValue>
    {
        private ValueDefinition source;
        private string timeframe;
        private int candlesAgo;

        [JsonConstructor]
        public PreviousValue(ValueDefinition Source, string Timeframe, int CandlesAgo = 1)
        {
            this.source = Source;
            this.timeframe = Timeframe;
            this.candlesAgo = CandlesAgo;
        }

        [JsonRequired]
        public ValueDefinition Source
        {
            get
            {
                return this.source;
            }
            init
            {
                this.source = value;
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

        public bool Equals(PreviousValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<ValueDefinition>.Default.Equals(Source, other.Source)
                && EqualityComparer<string>.Default.Equals(Timeframe, other.Timeframe)
                && EqualityComparer<int>.Default.Equals(CandlesAgo, other.CandlesAgo);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as PreviousValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(PreviousValue));
            hash.Add(Source);
            hash.Add(Timeframe);
            hash.Add(CandlesAgo);
            return hash.ToHashCode();
        }

        public void Deconstruct(out ValueDefinition Source, out string Timeframe, out int CandlesAgo)
        {
            Source = this.Source;
            Timeframe = this.Timeframe;
            CandlesAgo = this.CandlesAgo;
        }
    }
}


