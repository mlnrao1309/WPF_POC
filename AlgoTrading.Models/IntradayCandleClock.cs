using System;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    public sealed class IntradayCandleClock
    {
        public DateTimeOffset GetCompletion(DateTimeOffset candleStart, TimeSpan duration, TradingSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (duration <= TimeSpan.Zero || duration > session.ClosedAt - session.OpenedAt)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "An intraday duration must be positive and no longer than its session.");
            }

            if (candleStart < session.OpenedAt || candleStart >= session.ClosedAt)
            {
                throw new ArgumentException("The candle start must lie within its session.", nameof(candleStart));
            }

            long elapsedTicks = (candleStart - session.OpenedAt).Ticks;
            if (elapsedTicks % duration.Ticks != 0)
            {
                throw new ArgumentException("The candle start must align with the duration measured from session open.", nameof(candleStart));
            }

            if (duration >= session.ClosedAt - candleStart)
            {
                return session.ClosedAt;
            }

            return MarketTimestamp.FromDateTime(candleStart.Add(duration).UtcDateTime);
        }

        /// <summary>
        /// Aggregation may seal one shortened bar when an explicit special session is shorter than the configured
        /// target. The ordinary candle-completion contract remains strict.
        /// </summary>
        public DateTimeOffset GetAggregationCompletion(DateTimeOffset candleStart, TimeSpan duration,
            TradingSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "An intraday duration must be positive.");
            }

            if (duration > session.ClosedAt - session.OpenedAt)
            {
                if (candleStart != session.OpenedAt)
                {
                    throw new ArgumentException("A target longer than its session must begin at session open.",
                        nameof(candleStart));
                }

                return session.ClosedAt;
            }

            return this.GetCompletion(candleStart, duration, session);
        }

        /// <summary>Provider finalization is an explicit prerequisite; wall-clock passage alone is insufficient.</summary>
        public bool TryCreateCompleted(Candle candle, TimeSpan duration, TradingSession session,
            DateTimeOffset observedAt, bool providerFinalized, out CompletedCandle? completedCandle)
        {
            completedCandle = null;
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (candle.InstrumentToken != session.InstrumentToken)
            {
                throw new ArgumentException("The candle and session must belong to the same instrument.", nameof(candle));
            }

            DateTimeOffset completion = GetCompletion(candle.OpenedAt, duration, session);
            if (candle.High < candle.Low || candle.High < Math.Max(candle.Open, candle.Close)
                || candle.Low > Math.Min(candle.Open, candle.Close) || candle.Volume < 0)
            {
                throw new ArgumentException("The candle contains invalid prices or volume.", nameof(candle));
            }

            if (!providerFinalized || completion > observedAt)
            {
                return false;
            }

            completedCandle = new CompletedCandle(candle, completion);
            return true;
        }
    }
}
