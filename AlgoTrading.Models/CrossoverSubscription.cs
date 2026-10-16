namespace AlgoTrading.Models.Rules
{
    internal sealed class CrossoverSubscription : IEquatable<CrossoverSubscription>
    {
        private string storedPath;
        private string storedTimeframe;
        private ComparisonCondition storedCondition;
        public CrossoverSubscription(string Path, string Timeframe, ComparisonCondition Condition)
        {
            storedPath = Path;
            storedTimeframe = Timeframe;
            storedCondition = Condition;
        }

        public string Path
        {
            get
            {
                return storedPath;
            }

            init
            {
                storedPath = value;
            }
        }

        public string Timeframe
        {
            get
            {
                return storedTimeframe;
            }

            init
            {
                storedTimeframe = value;
            }
        }

        public ComparisonCondition Condition
        {
            get
            {
                return storedCondition;
            }

            init
            {
                storedCondition = value;
            }
        }

        public bool Equals(CrossoverSubscription? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<string>.Default.Equals(Path, other.Path) && EqualityComparer<string>.Default.Equals(Timeframe, other.Timeframe) && EqualityComparer<ComparisonCondition>.Default.Equals(Condition, other.Condition);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as CrossoverSubscription);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(CrossoverSubscription));
            hash.Add(Path);
            hash.Add(Timeframe);
            hash.Add(Condition);
            return hash.ToHashCode();
        }

        public static bool operator ==(CrossoverSubscription? left, CrossoverSubscription? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(CrossoverSubscription? left, CrossoverSubscription? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out string Path, out string Timeframe, out ComparisonCondition Condition)
        {
            Path = this.Path;
            Timeframe = this.Timeframe;
            Condition = this.Condition;
        }
    }
}

