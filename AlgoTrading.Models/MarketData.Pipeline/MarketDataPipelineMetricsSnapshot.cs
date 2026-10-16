namespace AlgoTrading.Models.MarketData.Pipeline
{
    public sealed class MarketDataPipelineMetricsSnapshot
    {
        public MarketDataPipelineMetricsSnapshot(long queueDepth, long acceptedEvents, long processedEvents,
            long rejectedEvents, long droppedEvents, long retryCount, long failureCount, long unprocessedOnShutdown,
            TimeSpan totalProcessingDuration, TimeSpan maximumProcessingDuration)
        {
            this.QueueDepth = queueDepth;
            this.AcceptedEvents = acceptedEvents;
            this.ProcessedEvents = processedEvents;
            this.RejectedEvents = rejectedEvents;
            this.DroppedEvents = droppedEvents;
            this.RetryCount = retryCount;
            this.FailureCount = failureCount;
            this.UnprocessedOnShutdown = unprocessedOnShutdown;
            this.TotalProcessingDuration = totalProcessingDuration;
            this.MaximumProcessingDuration = maximumProcessingDuration;
        }

        public long QueueDepth { get; }

        public long AcceptedEvents { get; }

        public long ProcessedEvents { get; }

        public long RejectedEvents { get; }

        public long DroppedEvents { get; }

        public long RetryCount { get; }

        public long FailureCount { get; }

        public long UnprocessedOnShutdown { get; }

        public TimeSpan TotalProcessingDuration { get; }

        public TimeSpan MaximumProcessingDuration { get; }
    }
}
