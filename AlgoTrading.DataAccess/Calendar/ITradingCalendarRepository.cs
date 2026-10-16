using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    public interface ITradingCalendarRepository
    {
        Task<bool> HasCalendarDaysAsync(CancellationToken cancellationToken = default);
        Task SaveSourceAsync(CalendarSourceDocument source, CancellationToken cancellationToken = default);
        Task PublishAsync(ExchangeCalendarSnapshot calendar, CancellationToken cancellationToken = default);
        Task<ExchangeCalendarSnapshot?> LoadAsync(string exchangeCode, string segmentCode, string revision,
            DateTimeOffset asOf, CancellationToken cancellationToken = default);
    }
}
