using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CriticalReferenceProjector
    {
        public CriticalReferenceProjection Project(CriticalLevelOrigin origin, TimeframeDefinition timeframe,
            IReadOnlyList<CompletedCandle> sourceCandles, ITradingSessionCalendar calendar, DateTimeOffset asOf)
        {
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentNullException.ThrowIfNull(sourceCandles);
            ArgumentNullException.ThrowIfNull(calendar);
            TimeframeValidation.RequireIntraday(timeframe);
            if (!origin.IsActiveAt(asOf))
            {
                throw new InvalidOperationException("Critical references are not yet active.");
            }
            DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(origin.Source.Candle.OpenedAt));
            CalendarLookupResult lookup = calendar.GetSession(origin.Source.Candle.InstrumentToken, date);
            if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null)
            {
                throw new InvalidOperationException("Source session calendar is unavailable.");
            }
            CandleAggregator aggregator = new CandleAggregator(new IntradayCandleClock());
            CandleAggregationRequest request = new CandleAggregationRequest(timeframe, lookup.Session);
            IReadOnlyList<CompletedCandle> projected = aggregator.Aggregate(sourceCandles, request);
            if (projected.Count == 0 || projected[0].Candle.OpenedAt != lookup.Session.OpenedAt
                || projected[projected.Count - 1].ClosedAt != lookup.Session.ClosedAt)
            {
                throw new InvalidOperationException("Complete intraday source coverage is required; daily OHLC is not a fallback.");
            }
            return new CriticalReferenceProjection(origin, timeframe, projected[0], projected[projected.Count - 1]);
        }
    }
}
