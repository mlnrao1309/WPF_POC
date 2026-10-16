using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    /// <summary>Runtime calendar boundary used by strategy and market-data services.</summary>
    public sealed class RepositoryTradingCalendarProvider : ITradingCalendarProvider
    {
        private readonly ITradingCalendarRepository repository;

        public RepositoryTradingCalendarProvider(ITradingCalendarRepository repository)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<ITradingSessionCalendar> LoadAsync(TradingCalendarSelection selection, int instrumentToken,
            DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (instrumentToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instrumentToken));
            }

            ExchangeCalendarSnapshot? snapshot = await repository.LoadAsync(selection.ExchangeCode,
                selection.SegmentCode, selection.Revision, asOf, cancellationToken);
            if (snapshot == null)
            {
                throw new KeyNotFoundException(
                    $"Calendar {selection.ExchangeCode}/{selection.SegmentCode}/{selection.Revision} was not available at {asOf:O}.");
            }
            return snapshot.ForInstrument(instrumentToken);
        }
    }
}
