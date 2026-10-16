namespace AlgoTrading.Models.MarketData.Pipeline
{
    internal sealed class MarketDataPipelineMetrics
    {
        private long queueDepth;
        private long acceptedEvents;
        private long processedEvents;
        private long rejectedEvents;
        private long droppedEvents;
        private long retryCount;
        private long failureCount;
        private long unprocessedOnShutdown;
        private long totalProcessingTicks;
        private long maximumProcessingTicks;

        internal void Accepted()
        {
            Interlocked.Increment(ref this.acceptedEvents);
            Interlocked.Increment(ref this.queueDepth);
        }

        internal void Processed(TimeSpan duration)
        {
            Interlocked.Increment(ref this.processedEvents);
            Interlocked.Decrement(ref this.queueDepth);
            this.RecordDuration(duration);
        }

        internal void Rejected()
        {
            Interlocked.Increment(ref this.rejectedEvents);
        }

        internal void Retried()
        {
            Interlocked.Increment(ref this.retryCount);
        }

        internal void Failed(TimeSpan duration)
        {
            Interlocked.Increment(ref this.failureCount);
            Interlocked.Decrement(ref this.queueDepth);
            this.RecordDuration(duration);
        }

        internal void Unprocessed()
        {
            Interlocked.Increment(ref this.droppedEvents);
            Interlocked.Increment(ref this.unprocessedOnShutdown);
            Interlocked.Decrement(ref this.queueDepth);
        }

        internal MarketDataPipelineMetricsSnapshot Snapshot()
        {
            return new MarketDataPipelineMetricsSnapshot(
                Interlocked.Read(ref this.queueDepth),
                Interlocked.Read(ref this.acceptedEvents),
                Interlocked.Read(ref this.processedEvents),
                Interlocked.Read(ref this.rejectedEvents),
                Interlocked.Read(ref this.droppedEvents),
                Interlocked.Read(ref this.retryCount),
                Interlocked.Read(ref this.failureCount),
                Interlocked.Read(ref this.unprocessedOnShutdown),
                TimeSpan.FromTicks(Interlocked.Read(ref this.totalProcessingTicks)),
                TimeSpan.FromTicks(Interlocked.Read(ref this.maximumProcessingTicks)));
        }

        private void RecordDuration(TimeSpan duration)
        {
            Interlocked.Add(ref this.totalProcessingTicks, duration.Ticks);
            long observed = Interlocked.Read(ref this.maximumProcessingTicks);
            while (duration.Ticks > observed)
            {
                long original = Interlocked.CompareExchange(ref this.maximumProcessingTicks, duration.Ticks, observed);
                if (original == observed)
                {
                    return;
                }

                observed = original;
            }
        }
    }
}
