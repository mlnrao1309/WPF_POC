namespace AlgoTrading.Models.MarketData.Processing
{
    public sealed class ClosedCandleProcessingResult
    {
        public ClosedCandleProcessingResult(ClosedCandleProcessingStatus status, string diagnostic,
            string calendarRevision, IReadOnlyList<CompletedTargetCandle> completedCandles)
        {
            this.Status = status;
            this.Diagnostic = diagnostic;
            this.CalendarRevision = calendarRevision;
            this.CompletedCandles = completedCandles;
        }

        public ClosedCandleProcessingStatus Status { get; }

        public string Diagnostic { get; }

        public string CalendarRevision { get; }

        public IReadOnlyList<CompletedTargetCandle> CompletedCandles { get; }
    }
}
