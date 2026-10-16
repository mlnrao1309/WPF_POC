using AlgoTrading.Models;

namespace AlgoTrading.DataAccess.Calendar
{
    /// <summary>Expands official NSE observations into one explicit row for every calendar date.</summary>
    public sealed class NseTradingCalendarParser
    {
        public ExchangeCalendarSnapshot Parse(HolidaySourceReport report, NseCalendarImportRequest request)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(request);
            if (report.Year != request.Year || !string.Equals(report.SegmentCode, request.SegmentCode, StringComparison.Ordinal))
            {
                throw new ArgumentException("The report does not match the requested segment and year.", nameof(report));
            }

            Dictionary<DateOnly, ExchangeHoliday> holidays = report.Holidays.ToDictionary(value => value.Date);
            Dictionary<DateOnly, NseSpecialSession> specials = new Dictionary<DateOnly, NseSpecialSession>();
            foreach (NseSpecialSession special in request.SpecialSessions)
            {
                if (special.Date.Year != request.Year || !specials.TryAdd(special.Date, special))
                {
                    throw new InvalidDataException("Special sessions must be unique and inside the requested year.");
                }
            }

            DateOnly firstDate = new DateOnly(request.Year, 1, 1);
            DateOnly lastDate = new DateOnly(request.Year, 12, 31);
            List<ExchangeCalendarDay> days = new List<ExchangeCalendarDay>(lastDate.DayNumber - firstDate.DayNumber + 1);
            for (DateOnly date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (specials.TryGetValue(date, out NseSpecialSession? special))
                {
                    days.Add(new ExchangeCalendarDay(date, special.OpensAt, special.ClosesAt,
                        $"NSE special session: {special.Reason}"));
                }
                else if (holidays.TryGetValue(date, out ExchangeHoliday? holiday))
                {
                    days.Add(new ExchangeCalendarDay(date, null, null, $"NSE holiday: {holiday.Description}"));
                }
                else if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    days.Add(new ExchangeCalendarDay(date, null, null, "Weekend"));
                }
                else
                {
                    days.Add(new ExchangeCalendarDay(date, request.RegularOpen, request.RegularClose, "NSE regular session"));
                }
            }

            return new ExchangeCalendarSnapshot(Guid.NewGuid(), "NSE", request.SegmentCode, request.Revision,
                firstDate, lastDate, report.Source.SourceId, days);
        }
    }
}
