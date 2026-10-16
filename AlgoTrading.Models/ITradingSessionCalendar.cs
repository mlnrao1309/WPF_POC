using System;

namespace AlgoTrading.Models
{
    public interface ITradingSessionCalendar
    {
        string Revision { get; }

        CalendarLookupResult GetSession(int instrumentToken, DateOnly date);
        CalendarLookupResult GetNextSession(int instrumentToken, DateTimeOffset after);
    }
}
