using System.Globalization;

namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>Outcome of one historical backfill pass across every requested instrument and source stream.</summary>
    public sealed class HistoricalBackfillReport
    {
        private readonly List<string> failures;

        public HistoricalBackfillReport(int requestedChunks, int completedChunks, int storedChunks, bool budgetExhausted,
            List<string> failures)
        {
            this.RequestedChunks = requestedChunks;
            this.CompletedChunks = completedChunks;
            this.StoredChunks = storedChunks;
            this.BudgetExhausted = budgetExhausted;
            this.failures = failures;
        }

        public int RequestedChunks { get; }

        public int CompletedChunks { get; }

        public int StoredChunks { get; }

        public bool BudgetExhausted { get; }

        public IReadOnlyList<string> Failures
        {
            get
            {
                return this.failures.AsReadOnly();
            }
        }

        public bool IsComplete
        {
            get
            {
                return this.failures.Count == 0 && !this.BudgetExhausted;
            }
        }

        public void ThrowIfIncomplete()
        {
            if (this.failures.Count == 0)
            {
                return;
            }

            const int maximumReported = 10;
            int reported = maximumReported;
            if (this.failures.Count < maximumReported)
            {
                reported = this.failures.Count;
            }
            List<string> lines = new List<string>();
            for (int index = 0; index < reported; index++)
            {
                lines.Add(this.failures[index]);
            }

            string suffix = string.Empty;
            if (this.failures.Count > reported)
            {
                suffix = " (+" + (this.failures.Count - reported).ToString(CultureInfo.InvariantCulture) + " more)";
            }

            throw new InvalidOperationException("Historical backfill failed for "
                + this.failures.Count.ToString(CultureInfo.InvariantCulture) + " chunk(s): "
                + string.Join(" | ", lines) + suffix);
        }
    }
}
