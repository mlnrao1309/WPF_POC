namespace AlgoTrading.Models.MarketData.Ingestion
{
    /// <summary>The last contiguously completed historical chunk for one instrument and source stream.</summary>
    public sealed class HistoricalIngestionCheckpoint
    {
        public HistoricalIngestionCheckpoint(long instrumentToken, string streamType, DateTime chunkStart, DateTime chunkEnd)
        {
            this.InstrumentToken = instrumentToken;
            this.StreamType = streamType;
            this.ChunkStart = chunkStart;
            this.ChunkEnd = chunkEnd;
        }

        public long InstrumentToken { get; }

        public string StreamType { get; }

        public DateTime ChunkStart { get; }

        public DateTime ChunkEnd { get; }
    }
}
