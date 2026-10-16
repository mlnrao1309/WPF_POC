namespace AlgoTrading.Models.Rules
{
    internal sealed class PendingCrossover : IEquatable<PendingCrossover>
    {
        private CrossoverSubscription storedSubscription;
        private CrossoverOccurrence storedOccurrence;
        public PendingCrossover(CrossoverSubscription Subscription, CrossoverOccurrence Occurrence)
        {
            storedSubscription = Subscription;
            storedOccurrence = Occurrence;
        }

        public CrossoverSubscription Subscription
        {
            get
            {
                return storedSubscription;
            }

            init
            {
                storedSubscription = value;
            }
        }

        public CrossoverOccurrence Occurrence
        {
            get
            {
                return storedOccurrence;
            }

            init
            {
                storedOccurrence = value;
            }
        }

        public bool Equals(PendingCrossover? other)
        {
            if (other is null)
            {
                return false;
            }

            return EqualityComparer<CrossoverSubscription>.Default.Equals(Subscription, other.Subscription) && EqualityComparer<CrossoverOccurrence>.Default.Equals(Occurrence, other.Occurrence);
        }

        public override bool Equals(object? other)
        {
            return Equals(other as PendingCrossover);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(typeof(PendingCrossover));
            hash.Add(Subscription);
            hash.Add(Occurrence);
            return hash.ToHashCode();
        }

        public static bool operator ==(PendingCrossover? left, PendingCrossover? right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(PendingCrossover? left, PendingCrossover? right)
        {
            return !object.Equals(left, right);
        }

        public void Deconstruct(out CrossoverSubscription Subscription, out CrossoverOccurrence Occurrence)
        {
            Subscription = this.Subscription;
            Occurrence = this.Occurrence;
        }
    }
}

