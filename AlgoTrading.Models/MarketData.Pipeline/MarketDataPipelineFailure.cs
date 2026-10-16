namespace AlgoTrading.Models.MarketData.Pipeline
{
    public sealed class MarketDataPipelineFailure
    {
        public MarketDataPipelineFailure(string sourceIdentity, int instrumentToken, int attempts, string message)
        {
            this.SourceIdentity = sourceIdentity;
            this.InstrumentToken = instrumentToken;
            this.Attempts = attempts;
            this.Message = message;
        }

        public string SourceIdentity { get; }

        public int InstrumentToken { get; }

        public int Attempts { get; }

        public string Message { get; }
    }
}
