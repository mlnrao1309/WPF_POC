using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    public interface ITradingCalendarProvider
    {
        Task<ITradingSessionCalendar> LoadAsync(TradingCalendarSelection selection, int instrumentToken,
            DateTimeOffset asOf, CancellationToken cancellationToken = default);
    }
}
