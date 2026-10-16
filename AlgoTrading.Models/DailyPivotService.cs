using System;
using System.Globalization;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models
{
    /// <summary>Global daily pivot and qualification calculation, independent of strategy indicators.</summary>
    public sealed class DailyPivotService
    {
        private readonly IDailyPivotFormula formula;

        public DailyPivotService() : this(new SqlRangeExtensionPivotCalculator())
        {
        }

        public DailyPivotService(IDailyPivotFormula formula)
        {
            ArgumentNullException.ThrowIfNull(formula);
            this.formula = formula;
        }

        public DailyPivotResult CalculateOrdinary(CompletedCandle source, ITradingSessionCalendar calendar, DateTimeOffset asOf)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(calendar);
            ValidateDaily(source, calendar);
            if (source.ClosedAt > asOf)
            {
                throw new InvalidOperationException("The source session is not completed.");
            }
            CalendarLookupResult next = calendar.GetNextSession(source.Candle.InstrumentToken, source.ClosedAt);
            if (next.Status != CalendarLookupStatus.Found || next.Session == null)
            {
                throw new InvalidOperationException("The effective pivot session is unavailable.");
            }
            if (asOf < next.Session.OpenedAt || asOf > next.Session.ClosedAt)
            {
                throw new InvalidOperationException("The ordinary pivot is outside its effective session.");
            }
            DailyPivotNumbers numbers = this.formula.Calculate(source.Candle.High, source.Candle.Low, source.Candle.Close,
                source.Candle.Close, source.Candle.Close);
            return new DailyPivotResult(source, next.Session, numbers.Pivots, null, null, null);
        }

        public DailyPivotResult Calculate(CompletedCandle source, CompletedCandle qualification,
            ITradingSessionCalendar calendar, DateTimeOffset asOf)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(qualification);
            ArgumentNullException.ThrowIfNull(calendar);
            ValidateDaily(source, calendar);
            if (source.Candle.InstrumentToken != qualification.Candle.InstrumentToken)
            {
                throw new ArgumentException("Source and qualification instruments must match.");
            }
            if (source.ClosedAt > asOf)
            {
                throw new InvalidOperationException("The source session is not yet completed.");
            }
            CalendarLookupResult next = calendar.GetNextSession(source.Candle.InstrumentToken, source.ClosedAt);
            TradingSession? effective = next.Session;
            if (next.Status != CalendarLookupStatus.Found || effective == null
                || effective.OpenedAt != qualification.Candle.OpenedAt || effective.ClosedAt != qualification.ClosedAt)
            {
                throw new InvalidOperationException("Qualification must be the next known trading session after the source.");
            }
            if (asOf < effective.OpenedAt)
            {
                throw new InvalidOperationException("The ordinary pivot interval has not started.");
            }
            SqlRangeExtensionPivotCalculator calculator = new SqlRangeExtensionPivotCalculator();
            SqlRangeExtensionPivots pivots = calculator.Calculate(source.Candle.High, source.Candle.Low, source.Candle.Close);
            if (asOf < qualification.ClosedAt)
            {
                return new DailyPivotResult(source, effective, pivots, null, null, null);
            }
            ValidateDaily(qualification, calendar);
            DailyPivotNumbers numbers = this.formula.Calculate(source.Candle.High, source.Candle.Low, source.Candle.Close,
                qualification.Candle.Open, qualification.Candle.Close);
            pivots = numbers.Pivots;
            bool ppr1 = numbers.Ppr1;
            bool pps1 = numbers.Pps1;
            CriticalLevelOrigin? origin = null;
            if (ppr1 || pps1)
            {
                CalendarLookupResult activation = calendar.GetNextSession(source.Candle.InstrumentToken, qualification.ClosedAt);
                if (activation.Status != CalendarLookupStatus.Found || activation.Session == null)
                {
                    throw new InvalidOperationException("Critical activation requires the next known session; no weekday is assumed.");
                }
                string identity = "SqlRangeExtensionV1:Daily:"
                    + source.Candle.InstrumentToken.ToString(CultureInfo.InvariantCulture) + ":"
                    + source.Candle.OpenedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":"
                    + qualification.Candle.OpenedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
                origin = new CriticalLevelOrigin(identity, source, qualification, ppr1, pps1, activation.Session.OpenedAt);
            }
            return new DailyPivotResult(source, effective, pivots, ppr1, pps1, origin);
        }

        private void ValidateDaily(CompletedCandle candle, ITradingSessionCalendar calendar)
        {
            TimeframeNormalizer normalizer = new TimeframeNormalizer();
            if (normalizer.Normalize(candle.Candle.TimeframeMinutes).Kind != TimeframeKind.Daily)
            {
                throw new ArgumentException("Daily qualification requires daily candles.");
            }
            Candle prices = candle.Candle;
            if (prices.High < prices.Low || prices.Open < prices.Low || prices.Open > prices.High
                || prices.Close < prices.Low || prices.Close > prices.High || prices.Volume < 0)
            {
                throw new ArgumentException("Daily candle OHLC or volume is invalid.");
            }
            DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(prices.OpenedAt));
            CalendarLookupResult lookup = calendar.GetSession(prices.InstrumentToken, date);
            if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null
                || lookup.Session.OpenedAt != prices.OpenedAt || lookup.Session.ClosedAt != candle.ClosedAt)
            {
                throw new InvalidOperationException("The daily candle does not match a known complete calendar session.");
            }
        }
    }
}
