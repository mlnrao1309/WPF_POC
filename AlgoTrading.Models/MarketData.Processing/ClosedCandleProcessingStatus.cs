namespace AlgoTrading.Models.MarketData.Processing
{
    public enum ClosedCandleProcessingStatus
    {
        Accepted,
        Duplicate,
        RejectedNotFinalized,
        RejectedMalformed,
        RejectedInstrument,
        RejectedTimeframe,
        RejectedOutOfOrder,
        ClosedSession,
        CalendarUnavailable,
        IncompleteCoverage
    }
}
