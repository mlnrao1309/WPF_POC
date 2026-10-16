namespace AlgoTrading.Models.Rules
{
    internal sealed class CrossoverOccurrenceComparer : IComparer<CrossoverOccurrence>
    {
        private static readonly CrossoverOccurrenceComparer instance = new CrossoverOccurrenceComparer();
        internal static CrossoverOccurrenceComparer Instance
        {
            get
            {
                return instance;
            }
        }

        public int Compare(CrossoverOccurrence? first, CrossoverOccurrence? second)
        {
            if (ReferenceEquals(first, second))
            {
                return 0;
            }

            if (first == null)
            {
                return -1;
            }

            if (second == null)
            {
                return 1;
            }

            int timestampOrder = first.OccurredAt.CompareTo(second.OccurredAt);
            if (timestampOrder != 0)
            {
                return timestampOrder;
            }

            return StringComparer.Ordinal.Compare(first.ConditionPath, second.ConditionPath);
        }
    }
}

