namespace AlgoTrading.Models.MarketData.Pipeline
{
    public sealed class MarketDataPipelineOptions
    {
        public MarketDataPipelineOptions(int capacity, int shardCount, int maximumSinkAttempts,
            TimeSpan sinkRetryDelay, int deduplicationHistoryCapacity = 100000)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (shardCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shardCount));
            }

            if (maximumSinkAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSinkAttempts));
            }

            if (sinkRetryDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(sinkRetryDelay));
            }

            if (deduplicationHistoryCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deduplicationHistoryCapacity));
            }

            this.Capacity = capacity;
            this.ShardCount = shardCount;
            this.MaximumSinkAttempts = maximumSinkAttempts;
            this.SinkRetryDelay = sinkRetryDelay;
            this.DeduplicationHistoryCapacity = deduplicationHistoryCapacity;
        }

        public int Capacity { get; }

        public int ShardCount { get; }

        public int MaximumSinkAttempts { get; }

        public TimeSpan SinkRetryDelay { get; }

        public int DeduplicationHistoryCapacity { get; }
    }
}
