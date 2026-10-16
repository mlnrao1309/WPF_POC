namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>
    /// Explicit requested and provider-supported coverage. Correction runs deliberately bypass an old checkpoint;
    /// ordinary runs resume immediately after it.
    /// </summary>
    public sealed class HistoricalBackfillRequest
    {
        public HistoricalBackfillRequest(DateTime requestedStart, DateTime requestedEnd, DateTime sourceCoverageStart,
            DateTime sourceCoverageEnd, IReadOnlyList<string> streamTypes, TimeSpan budget, bool refreshCorrections)
        {
            if (requestedEnd < requestedStart)
            {
                throw new ArgumentException("Requested end must not precede requested start.", nameof(requestedEnd));
            }

            if (sourceCoverageEnd < sourceCoverageStart)
            {
                throw new ArgumentException("Source coverage end must not precede its start.", nameof(sourceCoverageEnd));
            }

            if (streamTypes == null || streamTypes.Count == 0)
            {
                throw new ArgumentException("At least one historical source stream is required.", nameof(streamTypes));
            }

            if (budget <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(budget), "Historical backfill budget must be positive.");
            }

            List<string> validatedStreams = new List<string>();
            for (int index = 0; index < streamTypes.Count; index++)
            {
                string streamType = streamTypes[index];
                HistoricalStreamTypes.Validate(streamType);
                if (!validatedStreams.Contains(streamType, StringComparer.Ordinal))
                {
                    validatedStreams.Add(streamType);
                }
            }

            this.RequestedStart = requestedStart;
            this.RequestedEnd = requestedEnd;
            this.SourceCoverageStart = sourceCoverageStart;
            this.SourceCoverageEnd = sourceCoverageEnd;
            this.StreamTypes = validatedStreams.AsReadOnly();
            this.Budget = budget;
            this.RefreshCorrections = refreshCorrections;
        }

        public DateTime RequestedStart { get; }

        public DateTime RequestedEnd { get; }

        public DateTime SourceCoverageStart { get; }

        public DateTime SourceCoverageEnd { get; }

        public IReadOnlyList<string> StreamTypes { get; }

        public TimeSpan Budget { get; }

        public bool RefreshCorrections { get; }
    }
}
