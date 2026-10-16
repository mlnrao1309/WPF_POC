using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public sealed class CompletedCandle : IEquatable<CompletedCandle>
    {
        private Candle candle;
        private DateTimeOffset closedAt;

        [JsonConstructor]
        public CompletedCandle(Candle Candle, DateTimeOffset ClosedAt)
        {
            this.candle = Candle;
            this.closedAt = ClosedAt;
        }

        public Candle Candle
        {
            get
            {
                return this.candle;
            }
            init
            {
                this.candle = value;
            }
        }

        public DateTimeOffset ClosedAt
        {
            get
            {
                return this.closedAt;
            }
            init
            {
                this.closedAt = value;
            }
        }

        public bool Equals(CompletedCandle? other)
        {
            if (other is null)
            {
                return false;
            }
            return EqualityComparer<Candle>.Default.Equals(Candle, other.Candle)
                && EqualityComparer<DateTimeOffset>.Default.Equals(ClosedAt, other.ClosedAt);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CompletedCandle);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CompletedCandle));
            hash.Add(Candle);
            hash.Add(ClosedAt);
            return hash.ToHashCode();
        }

        public static bool operator ==(CompletedCandle? left, CompletedCandle? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(CompletedCandle? left, CompletedCandle? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out Candle Candle, out DateTimeOffset ClosedAt)
        {
            Candle = this.Candle;
            ClosedAt = this.ClosedAt;
        }
    }
}


