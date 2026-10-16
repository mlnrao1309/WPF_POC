using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class CandleValue : ValueDefinition, IEquatable<CandleValue>
    {
        private string timeframe;
        private CandleField field;
        private int candlesAgo;

        [JsonConstructor]
        public CandleValue(string Timeframe, CandleField Field = CandleField.Close, int CandlesAgo = 0)
        {
            this.timeframe = Timeframe;
            this.field = Field;
            this.candlesAgo = CandlesAgo;
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

        public CandleField Field
        {
            get
            {
                return this.field;
            }
            init
            {
                this.field = value;
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

        public bool Equals(CandleValue? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<string>.Default.Equals(Timeframe, other.Timeframe)
                && EqualityComparer<CandleField>.Default.Equals(Field, other.Field)
                && EqualityComparer<int>.Default.Equals(CandlesAgo, other.CandlesAgo);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CandleValue);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CandleValue));
            hash.Add(Timeframe);
            hash.Add(Field);
            hash.Add(CandlesAgo);
            return hash.ToHashCode();
        }

        public void Deconstruct(out string Timeframe, out CandleField Field, out int CandlesAgo)
        {
            Timeframe = this.Timeframe;
            Field = this.Field;
            CandlesAgo = this.CandlesAgo;
        }
    }
}


