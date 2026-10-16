namespace AlgoTrading.Models.Rules
{
    public sealed class CrossoverEventArgs : EventArgs
    {
        private readonly CrossoverOccurrence occurrence;
        public CrossoverOccurrence Occurrence
        {
            get
            {
                return occurrence;
            }
        }

        public CrossoverEventArgs(CrossoverOccurrence occurrence)
        {
            ArgumentNullException.ThrowIfNull(occurrence);
            this.occurrence = occurrence;
        }
    }
}

