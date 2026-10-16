using System;
using System.Collections.Generic;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class CandleAggregator
    {
        private readonly IntradayCandleClock clock;

        public CandleAggregator(IntradayCandleClock clock)
        {
            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            this.clock = clock;
        }

        public IReadOnlyList<CompletedCandle> Aggregate(IReadOnlyList<CompletedCandle> sourceCandles,
            CandleAggregationRequest request)
        {
            if (sourceCandles == null)
            {
                throw new ArgumentNullException(nameof(sourceCandles));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            TimeframeDefinition target = request.TargetTimeframe;
            TradingSession session = request.Session;
            TimeSpan? optionalDuration = target.Duration;
            if (!optionalDuration.HasValue)
            {
                throw new InvalidOperationException("The target timeframe has no duration.");
            }
            TimeSpan duration = optionalDuration.Value;
            Dictionary<DateTimeOffset, List<CompletedCandle>> groups = new Dictionary<DateTimeOffset, List<CompletedCandle>>();
            DateTimeOffset? previousSourceClose = null;
            for (int index = 0; index < sourceCandles.Count; index++)
            {
                CompletedCandle source = sourceCandles[index];
                ValidateSource(source, session, previousSourceClose);
                DateTimeOffset targetStart = GetTargetStart(source.Candle.OpenedAt, duration, session);
                DateTimeOffset targetClose = this.clock.GetAggregationCompletion(targetStart, duration, session);
                if (source.ClosedAt > targetClose)
                {
                    throw new InvalidOperationException("A source candle crosses a requested aggregation boundary; splitting it would invent prices.");
                }
                List<CompletedCandle>? group;
                if (!groups.TryGetValue(targetStart, out group))
                {
                    group = new List<CompletedCandle>();
                    groups.Add(targetStart, group);
                }
                group.Add(source);
                previousSourceClose = source.ClosedAt;
            }

            List<CompletedCandle> result = new List<CompletedCandle>();
            foreach (KeyValuePair<DateTimeOffset, List<CompletedCandle>> entry in groups)
            {
                result.Add(BuildAggregate(entry.Key, entry.Value, target, session));
            }
            return result.AsReadOnly();
        }

        private void ValidateSource(CompletedCandle source, TradingSession session, DateTimeOffset? previousSourceClose)
        {
            if (source == null)
            {
                throw new ArgumentException("Source candles cannot be null.");
            }
            CandleValidation.Validate(source);
            if (source.Candle.InstrumentToken != session.InstrumentToken)
            {
                throw new ArgumentException("Source and session instruments must match.");
            }
            if (source.Candle.OpenedAt < session.OpenedAt || source.Candle.OpenedAt >= session.ClosedAt
                || source.ClosedAt <= source.Candle.OpenedAt || source.ClosedAt > session.ClosedAt)
            {
                throw new ArgumentException("Source candle lies outside the supplied session or has invalid bounds.");
            }
            if (previousSourceClose.HasValue && source.Candle.OpenedAt != previousSourceClose.Value)
            {
                throw new InvalidOperationException("Source candles must be ordered and contiguous; missing intervals are unavailable.");
            }
        }

        private DateTimeOffset GetTargetStart(DateTimeOffset sourceStart, TimeSpan duration, TradingSession session)
        {
            long elapsedTicks = (sourceStart - session.OpenedAt).Ticks;
            long boundaryTicks = elapsedTicks - (elapsedTicks % duration.Ticks);
            return MarketTimestamp.FromDateTime(session.OpenedAt.AddTicks(boundaryTicks).UtcDateTime);
        }

        private CompletedCandle BuildAggregate(DateTimeOffset targetStart, List<CompletedCandle> group,
            TimeframeDefinition target, TradingSession session)
        {
            TimeSpan? optionalDuration = target.Duration;
            if (!optionalDuration.HasValue)
            {
                throw new InvalidOperationException("The target timeframe has no duration.");
            }
            DateTimeOffset targetClose = this.clock.GetAggregationCompletion(targetStart, optionalDuration.Value, session);
            CompletedCandle first = group[0];
            CompletedCandle last = group[group.Count - 1];
            if (first.Candle.OpenedAt != targetStart || last.ClosedAt != targetClose)
            {
                throw new InvalidOperationException("A requested interval is incomplete; no aggregate candle was fabricated.");
            }
            decimal high = first.Candle.High;
            decimal low = first.Candle.Low;
            long volume = 0;
            for (int index = 0; index < group.Count; index++)
            {
                CompletedCandle item = group[index];
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
            Candle aggregate = new Candle(session.InstrumentToken, target.CanonicalName,
                MarketTimestamp.ToDatabase(targetStart), first.Candle.Open, high, low, last.Candle.Close, volume);
            return new CompletedCandle(aggregate, targetClose);
        }
    }
}
