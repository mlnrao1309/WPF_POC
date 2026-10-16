using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CalendarCandleAggregator
    {
        public IReadOnlyList<CalendarAggregatedCandle> Aggregate(IReadOnlyList<CompletedCandle> dailyCandles,
            TimeframeDefinition target, ITradingSessionCalendar calendar, int instrumentToken)
        {
            if (dailyCandles == null)
            {
                throw new ArgumentNullException(nameof(dailyCandles));
            }
            TimeframeValidation.RequireCalendar(target);
            if (calendar == null)
            {
                throw new ArgumentNullException(nameof(calendar));
            }
            if (dailyCandles.Count == 0)
            {
                return new List<CalendarAggregatedCandle>().AsReadOnly();
            }

            SortedDictionary<DateOnly, CompletedCandle> byDate = IndexDailyCandles(dailyCandles, calendar, instrumentToken);
            SortedDictionary<DateOnly, List<CompletedCandle>> groups = new SortedDictionary<DateOnly, List<CompletedCandle>>();
            foreach (KeyValuePair<DateOnly, CompletedCandle> entry in byDate)
            {
                DateOnly periodStart = GetPeriodStart(entry.Key, target.Kind);
                List<CompletedCandle>? group;
                if (!groups.TryGetValue(periodStart, out group))
                {
                    group = new List<CompletedCandle>();
                    groups.Add(periodStart, group);
                }
                group.Add(entry.Value);
            }

            List<CalendarAggregatedCandle> result = new List<CalendarAggregatedCandle>();
            foreach (KeyValuePair<DateOnly, List<CompletedCandle>> group in groups)
            {
                DateOnly periodEnd = GetPeriodEnd(group.Key, target.Kind);
                result.Add(BuildAggregate(group.Key, periodEnd, group.Value, target, calendar, instrumentToken));
            }
            return result.AsReadOnly();
        }

        private SortedDictionary<DateOnly, CompletedCandle> IndexDailyCandles(IReadOnlyList<CompletedCandle> dailyCandles,
            ITradingSessionCalendar calendar, int instrumentToken)
        {
            SortedDictionary<DateOnly, CompletedCandle> result = new SortedDictionary<DateOnly, CompletedCandle>();
            for (int index = 0; index < dailyCandles.Count; index++)
            {
                CompletedCandle candle = dailyCandles[index];
                if (candle == null || candle.Candle.InstrumentToken != instrumentToken)
                {
                    throw new ArgumentException("Daily candles must be non-null and belong to the requested instrument.");
                }
                TimeframeDefinition sourceTimeframe = new TimeframeNormalizer().Normalize(candle.Candle.TimeframeMinutes);
                CandleValidation.Validate(candle);
                if (sourceTimeframe.Kind != TimeframeKind.Daily)
                {
                    throw new ArgumentException("Calendar aggregation requires completed daily source candles.");
                }
                DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(candle.Candle.OpenedAt));
                CalendarLookupResult lookup = calendar.GetSession(instrumentToken, date);
                if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null
                    || candle.Candle.OpenedAt != lookup.Session.OpenedAt || candle.ClosedAt != lookup.Session.ClosedAt)
                {
                    throw new InvalidOperationException("A daily source candle does not match its calendar session.");
                }
                if (!result.TryAdd(date, candle))
                {
                    throw new InvalidOperationException("Duplicate daily candle for a calendar date.");
                }
            }
            return result;
        }

        private DateOnly GetPeriodStart(DateOnly date, TimeframeKind kind)
        {
            if (kind == TimeframeKind.Weekly)
            {
                int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
                return date.AddDays(-daysSinceMonday);
            }
            if (kind == TimeframeKind.Monthly)
            {
                return new DateOnly(date.Year, date.Month, 1);
            }
            throw new ArgumentException("Only weekly and monthly calendar aggregation is supported.", nameof(kind));
        }

        private DateOnly GetPeriodEnd(DateOnly start, TimeframeKind kind)
        {
            if (kind == TimeframeKind.Weekly)
            {
                return start.AddDays(6);
            }
            if (kind == TimeframeKind.Monthly)
            {
                return start.AddMonths(1).AddDays(-1);
            }
            throw new ArgumentException("Only weekly and monthly calendar aggregation is supported.", nameof(kind));
        }

        private CalendarAggregatedCandle BuildAggregate(DateOnly periodStart, DateOnly periodEnd,
            List<CompletedCandle> observed, TimeframeDefinition target, ITradingSessionCalendar calendar, int instrumentToken)
        {
            List<CompletedCandle> constituents = new List<CompletedCandle>();
            DateOnly date = periodStart;
            while (date <= periodEnd)
            {
                CalendarLookupResult lookup = calendar.GetSession(instrumentToken, date);
                if (lookup.Status == CalendarLookupStatus.Unavailable)
                {
                    throw new InvalidOperationException("Calendar coverage is incomplete for the requested period.");
                }
                if (lookup.Status == CalendarLookupStatus.Found)
                {
                    CompletedCandle? constituent;
                    if (!TryFind(observed, date, out constituent) || constituent == null)
                    {
                        throw new InvalidOperationException("A completed trading session is missing from the calendar period.");
                    }
                    constituents.Add(constituent);
                }
                date = date.AddDays(1);
            }
            if (constituents.Count == 0)
            {
                throw new InvalidOperationException("A calendar period contains no trading sessions.");
            }
            CompletedCandle first = constituents[0];
            CompletedCandle last = constituents[constituents.Count - 1];
            decimal high = first.Candle.High;
            decimal low = first.Candle.Low;
            long volume = 0;
            for (int index = 0; index < constituents.Count; index++)
            {
                CompletedCandle item = constituents[index];
                if (item.Candle.High > high)
                {
                    high = item.Candle.High;
                }
                if (item.Candle.Low < low)
                {
                    low = item.Candle.Low;
                }
                volume = checked(volume + item.Candle.Volume);
            }
            Candle aggregateCandle = new Candle(instrumentToken, target.CanonicalName,
                MarketTimestamp.ToDatabase(first.Candle.OpenedAt), first.Candle.Open, high, low, last.Candle.Close, volume);
            CompletedCandle aggregate = new CompletedCandle(aggregateCandle, last.ClosedAt);
            return new CalendarAggregatedCandle(aggregate, first, last, periodStart, periodEnd, target);
        }

        private bool TryFind(IReadOnlyList<CompletedCandle> observed, DateOnly date, out CompletedCandle? result)
        {
            for (int index = 0; index < observed.Count; index++)
            {
                DateOnly observedDate = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(observed[index].Candle.OpenedAt));
                if (observedDate == date)
                {
                    result = observed[index];
                    return true;
                }
            }
            result = null;
            return false;
        }
    }
}
