namespace AlgoTrading.Models.Rules
{
    /// <summary>The evaluation-clock candle and the two compared expressions sampled at its close.</summary>
    public sealed class CrossoverCandleSnapshot : IEquatable<CrossoverCandleSnapshot>
    {
        private CompletedCandle storedCandle;
        private CrossoverValueSnapshot storedLeft;
        private CrossoverValueSnapshot storedRight;
        public CrossoverCandleSnapshot(CompletedCandle Candle, CrossoverValueSnapshot Left, CrossoverValueSnapshot Right)
        {
            storedCandle = Candle;
            storedLeft = Left;
            storedRight = Right;
        }

        public CompletedCandle Candle
        {
            get
            {
                return storedCandle;
            }

            init
            {
                storedCandle = value;
            }
        }

        public CrossoverValueSnapshot Left
        {
            get
            {
                return storedLeft;
            }

            init
            {
                storedLeft = value;
            }
        }

        public CrossoverValueSnapshot Right
        {
            get
            {
                return storedRight;
            }

            init
            {
                storedRight = value;
            }
        }

        public bool Equals(CrossoverCandleSnapshot? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<CompletedCandle>.Default.Equals(Candle, other.Candle) && EqualityComparer<CrossoverValueSnapshot>.Default.Equals(Left, other.Left) && EqualityComparer<CrossoverValueSnapshot>.Default.Equals(Right, other.Right);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CrossoverCandleSnapshot);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CrossoverCandleSnapshot));
            hash.Add(Candle);
            hash.Add(Left);
            hash.Add(Right);
            return hash.ToHashCode();
        }

        public static bool operator ==(CrossoverCandleSnapshot? left, CrossoverCandleSnapshot? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(CrossoverCandleSnapshot? left, CrossoverCandleSnapshot? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out CompletedCandle Candle, out CrossoverValueSnapshot Left, out CrossoverValueSnapshot Right)
        {
            Candle = this.Candle;
            Left = this.Left;
            Right = this.Right;
        }
    }
}

