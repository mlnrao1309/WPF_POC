namespace AlgoTrading.Models.MarketData.Pipeline
{
    public enum MarketDataEnqueueStatus
    {
        NotStarted,
        Accepted,
        Duplicate,
        RejectedNotFinalized,
        QueueFull,
        Stopped
    }
}
