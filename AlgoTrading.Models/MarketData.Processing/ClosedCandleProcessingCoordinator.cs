using System.Globalization;
using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Ingestion;
using AlgoTrading.Models.MarketData.Pipeline;
using AlgoTrading.Models.Rules;

namespace AlgoTrading.Models.MarketData.Processing
{
    /// <summary>
    /// Stateful single-instrument coordinator. Inputs are accepted in source-stream order; gaps invalidate the
    /// affected session/period and no partial candle is fabricated.
    /// </summary>
    public sealed class ClosedCandleProcessingCoordinator : IClosedCandleEventProcessor
    {
        private readonly int instrumentToken;
        private readonly ITradingSessionCalendar calendar;
        private readonly PlatformConfigurationSnapshot configuration;
        private readonly IntradayCandleClock clock;
        private readonly CandleAggregator intradayAggregator;
        private readonly CalendarCandleAggregator calendarAggregator;
        private readonly Dictionary<string, CompletedCandle> acceptedSourceIdentities;
        private readonly HashSet<string> emittedTargetIdentities;
        private readonly Dictionary<string, List<CompletedCandle>> intradayBuckets;
        private readonly Dictionary<string, List<CompletedCandle>> calendarBuckets;
        private DateTimeOffset? lastIntradayClose;
        private DateTimeOffset? lastDailyClose;
        private DateOnly? intradaySessionDate;
        private bool intradaySessionInvalid;

        public ClosedCandleProcessingCoordinator(int instrumentToken, ITradingSessionCalendar calendar,
            PlatformConfigurationSnapshot configuration)
        {
            if (instrumentToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instrumentToken));
            }

            if (calendar == null)
            {
                throw new ArgumentNullException(nameof(calendar));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            this.instrumentToken = instrumentToken;
            this.calendar = calendar;
            this.configuration = configuration;
            this.clock = new IntradayCandleClock();
            this.intradayAggregator = new CandleAggregator(this.clock);
            this.calendarAggregator = new CalendarCandleAggregator();
            this.acceptedSourceIdentities = new Dictionary<string, CompletedCandle>(StringComparer.Ordinal);
            this.emittedTargetIdentities = new HashSet<string>(StringComparer.Ordinal);
            this.intradayBuckets = new Dictionary<string, List<CompletedCandle>>(StringComparer.Ordinal);
            this.calendarBuckets = new Dictionary<string, List<CompletedCandle>>(StringComparer.Ordinal);
        }

        public string CalendarRevision
        {
            get
            {
                return this.calendar.Revision;
            }
        }

        public ClosedCandleProcessingResult Process(FinalizedSourceCandleEvent sourceEvent)
        {
            List<CompletedTargetCandle> outputs = new List<CompletedTargetCandle>();
            if (sourceEvent == null || sourceEvent.Candle == null)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed,
                    "A source event and candle are required.", outputs);
            }

            if (!sourceEvent.ProviderFinalized || sourceEvent.Candle.ClosedAt > sourceEvent.ObservedAt)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedNotFinalized,
                    "The provider has not finalized this candle at the observation instant.", outputs);
            }

            CompletedCandle source = sourceEvent.Candle;
            if (source.Candle.InstrumentToken != this.instrumentToken)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedInstrument,
                    "The source candle belongs to a different instrument.", outputs);
            }

            try
            {
                CandleValidation.Validate(source);
            }
            catch (Exception exception)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed, exception.Message, outputs);
            }

            string sourceIdentity = BuildSourceIdentity(sourceEvent.StreamType, source);
            CompletedCandle? priorSource;
            if (this.acceptedSourceIdentities.TryGetValue(sourceIdentity, out priorSource))
            {
                if (priorSource.Equals(source))
                {
                    return this.Result(ClosedCandleProcessingStatus.Duplicate,
                        "The finalized source identity was already processed.", outputs);
                }

                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed,
                    "A conflicting revision reused a finalized source identity; use historical correction ingestion.",
                    outputs);
            }

            TimeframeDefinition sourceTimeframe;
            try
            {
                sourceTimeframe = new TimeframeNormalizer().Normalize(source.Candle.TimeframeMinutes);
            }
            catch (Exception exception)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe, exception.Message, outputs);
            }

            if (sourceEvent.StreamType == HistoricalStreamTypes.IntradayFiveMinute)
            {
                if (sourceTimeframe.CanonicalName != "5minute")
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe,
                        "INTRADAY_5M accepts only completed 5-minute source candles.", outputs);
                }

                return this.ProcessIntraday(sourceIdentity, source, outputs);
            }

            if (sourceEvent.StreamType == HistoricalStreamTypes.DailyOneDay)
            {
                if (sourceTimeframe.Kind != TimeframeKind.Daily)
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe,
                        "DAILY_1D accepts only completed daily source candles.", outputs);
                }

                return this.ProcessDaily(sourceIdentity, source, outputs);
            }

            return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe,
                "The source stream is not supported.", outputs);
        }

        private ClosedCandleProcessingResult ProcessIntraday(string sourceIdentity, CompletedCandle source,
            List<CompletedTargetCandle> outputs)
        {
            DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(source.Candle.OpenedAt));
            CalendarLookupResult lookup = this.calendar.GetSession(this.instrumentToken, date);
            if (lookup.Status == CalendarLookupStatus.ClosedDay)
            {
                return this.Result(ClosedCandleProcessingStatus.ClosedSession,
                    "The selected calendar explicitly closes this date.", outputs);
            }

            if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null)
            {
                return this.Result(ClosedCandleProcessingStatus.CalendarUnavailable,
                    "The selected calendar has no session coverage for this date.", outputs);
            }

            TradingSession session = lookup.Session;
            if (!this.intradaySessionDate.HasValue || this.intradaySessionDate.Value != date)
            {
                this.ResetIntradaySession(date);
            }

            DateTimeOffset expectedSourceClose;
            try
            {
                expectedSourceClose = source.Candle.OpenedAt.AddMinutes(5);
                if (expectedSourceClose > session.ClosedAt)
                {
                    expectedSourceClose = session.ClosedAt;
                }
            }
            catch (Exception exception)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed, exception.Message, outputs);
            }

            if (source.ClosedAt != expectedSourceClose)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed,
                    "The source completion does not match the selected session.", outputs);
            }

            if (this.lastIntradayClose.HasValue)
            {
                if (source.Candle.OpenedAt < this.lastIntradayClose.Value)
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedOutOfOrder,
                        "The intraday source candle is out of order.", outputs);
                }

                if (source.Candle.OpenedAt > this.lastIntradayClose.Value)
                {
                    this.intradaySessionInvalid = true;
                    this.intradayBuckets.Clear();
                    return this.Result(ClosedCandleProcessingStatus.IncompleteCoverage,
                        "A 5-minute source candle is missing; the remaining session is unavailable.", outputs);
                }
            }
            else if (source.Candle.OpenedAt != session.OpenedAt)
            {
                this.intradaySessionInvalid = true;
                return this.Result(ClosedCandleProcessingStatus.IncompleteCoverage,
                    "The first observed source candle does not begin at session open.", outputs);
            }

            if (this.intradaySessionInvalid)
            {
                return this.Result(ClosedCandleProcessingStatus.IncompleteCoverage,
                    "The current session was invalidated by incomplete source coverage.", outputs);
            }

            ClosedCandleProcessingResult? boundaryFailure = this.ValidateIntradayBoundaries(source, session, outputs);
            if (boundaryFailure != null)
            {
                return boundaryFailure;
            }

            this.acceptedSourceIdentities.Add(sourceIdentity, source);
            this.lastIntradayClose = source.ClosedAt;
            this.EmitIntradayTargets(source, session, outputs);
            return this.Result(ClosedCandleProcessingStatus.Accepted, "Finalized intraday source candle accepted.",
                outputs);
        }

        private ClosedCandleProcessingResult? ValidateIntradayBoundaries(CompletedCandle source,
            TradingSession session, List<CompletedTargetCandle> outputs)
        {
            for (int index = 0; index < this.configuration.Timeframes.ActiveCodes.Count; index++)
            {
                string code = this.configuration.Timeframes.ActiveCodes[index];
                TimeframeOption option = this.configuration.Timeframes.GetOption(code);
                if (option.SourceStream != TimeframeSourceStream.IntradayFiveMinute)
                {
                    continue;
                }

                TimeframeDefinition? target;
                if (!this.configuration.Timeframes.TryResolve(code, out target) || target == null
                    || !target.Duration.HasValue)
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe,
                        "An active intraday target has no valid duration.", outputs);
                }

                DateTimeOffset targetStart = GetIntradayTargetStart(source.Candle.OpenedAt, target.Duration.Value,
                    session);
                DateTimeOffset targetClose = this.clock.GetAggregationCompletion(targetStart, target.Duration.Value,
                    session);
                if (source.ClosedAt > targetClose)
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedTimeframe,
                        "A source candle crosses target " + code + "; splitting is forbidden.", outputs);
                }
            }

            return null;
        }

        private void EmitIntradayTargets(CompletedCandle source, TradingSession session,
            List<CompletedTargetCandle> outputs)
        {
            for (int index = 0; index < this.configuration.Timeframes.ActiveCodes.Count; index++)
            {
                string code = this.configuration.Timeframes.ActiveCodes[index];
                TimeframeOption option = this.configuration.Timeframes.GetOption(code);
                if (option.SourceStream != TimeframeSourceStream.IntradayFiveMinute)
                {
                    continue;
                }

                TimeframeDefinition? target;
                if (!this.configuration.Timeframes.TryResolve(code, out target) || target == null
                    || !target.Duration.HasValue)
                {
                    continue;
                }

                List<CompletedCandle>? bucket;
                if (!this.intradayBuckets.TryGetValue(code, out bucket))
                {
                    bucket = new List<CompletedCandle>();
                    this.intradayBuckets.Add(code, bucket);
                }

                bucket.Add(source);
                DateTimeOffset targetStart = GetIntradayTargetStart(source.Candle.OpenedAt, target.Duration.Value,
                    session);
                DateTimeOffset targetClose = this.clock.GetAggregationCompletion(targetStart, target.Duration.Value,
                    session);
                if (source.ClosedAt == targetClose)
                {
                    CandleAggregationRequest request = new CandleAggregationRequest(target, session);
                    IReadOnlyList<CompletedCandle> aggregates = this.intradayAggregator.Aggregate(bucket, request);
                    if (aggregates.Count != 1)
                    {
                        throw new InvalidOperationException("One completed target bucket must produce one aggregate.");
                    }

                    this.AddOutput(code, aggregates[0], outputs);
                    bucket.Clear();
                }
            }
        }

        private ClosedCandleProcessingResult ProcessDaily(string sourceIdentity, CompletedCandle source,
            List<CompletedTargetCandle> outputs)
        {
            DateOnly date = DateOnly.FromDateTime(MarketTimestamp.ToDatabase(source.Candle.OpenedAt));
            CalendarLookupResult lookup = this.calendar.GetSession(this.instrumentToken, date);
            if (lookup.Status == CalendarLookupStatus.ClosedDay)
            {
                return this.Result(ClosedCandleProcessingStatus.ClosedSession,
                    "The selected calendar explicitly closes this date.", outputs);
            }

            if (lookup.Status != CalendarLookupStatus.Found || lookup.Session == null)
            {
                return this.Result(ClosedCandleProcessingStatus.CalendarUnavailable,
                    "The selected calendar has no session coverage for this date.", outputs);
            }

            TradingSession session = lookup.Session;
            if (source.Candle.OpenedAt != session.OpenedAt || source.ClosedAt != session.ClosedAt)
            {
                return this.Result(ClosedCandleProcessingStatus.RejectedMalformed,
                    "The daily source candle does not match the selected session.", outputs);
            }

            if (this.lastDailyClose.HasValue)
            {
                if (source.ClosedAt <= this.lastDailyClose.Value)
                {
                    return this.Result(ClosedCandleProcessingStatus.RejectedOutOfOrder,
                        "The daily source candle is out of order.", outputs);
                }
            }

            this.acceptedSourceIdentities.Add(sourceIdentity, source);
            this.lastDailyClose = source.ClosedAt;
            ClosedCandleProcessingResult? calendarFailure = this.EmitCalendarTargets(source, date, outputs);
            if (calendarFailure != null)
            {
                return calendarFailure;
            }

            return this.Result(ClosedCandleProcessingStatus.Accepted, "Finalized daily source candle accepted.",
                outputs);
        }

        private ClosedCandleProcessingResult? EmitCalendarTargets(CompletedCandle source, DateOnly date,
            List<CompletedTargetCandle> outputs)
        {
            for (int index = 0; index < this.configuration.Timeframes.ActiveCodes.Count; index++)
            {
                string code = this.configuration.Timeframes.ActiveCodes[index];
                TimeframeOption option = this.configuration.Timeframes.GetOption(code);
                if (option.SourceStream != TimeframeSourceStream.DailyOneDay)
                {
                    continue;
                }

                TimeframeDefinition? target;
                if (!this.configuration.Timeframes.TryResolve(code, out target) || target == null)
                {
                    continue;
                }

                if (target.Kind == TimeframeKind.Daily)
                {
                    this.AddOutput(code, source, outputs);
                    continue;
                }

                DateOnly periodStart = GetPeriodStart(date, target.Kind);
                DateOnly periodEnd = GetPeriodEnd(periodStart, target.Kind);
                string bucketKey = code + "|" + periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                List<CompletedCandle>? bucket;
                if (!this.calendarBuckets.TryGetValue(bucketKey, out bucket))
                {
                    bucket = new List<CompletedCandle>();
                    this.calendarBuckets.Add(bucketKey, bucket);
                }

                bucket.Add(source);
                bool calendarUnavailable;
                bool isLastSession = this.IsLastTradingSession(date, periodEnd, out calendarUnavailable);
                if (calendarUnavailable)
                {
                    return this.Result(ClosedCandleProcessingStatus.CalendarUnavailable,
                        "Calendar coverage does not reach the end of target " + code + ".", outputs);
                }

                if (!isLastSession)
                {
                    continue;
                }

                try
                {
                    IReadOnlyList<CalendarAggregatedCandle> aggregates = this.calendarAggregator.Aggregate(bucket,
                        target, this.calendar, this.instrumentToken);
                    if (aggregates.Count != 1)
                    {
                        return this.Result(ClosedCandleProcessingStatus.IncompleteCoverage,
                            "The completed calendar period did not produce exactly one target.", outputs);
                    }

                    this.AddOutput(code, aggregates[0].Aggregate, outputs);
                }
                catch (InvalidOperationException exception)
                {
                    return this.Result(ClosedCandleProcessingStatus.IncompleteCoverage, exception.Message, outputs);
                }
                finally
                {
                    this.calendarBuckets.Remove(bucketKey);
                }
            }

            return null;
        }

        private bool IsLastTradingSession(DateOnly currentDate, DateOnly periodEnd, out bool calendarUnavailable)
        {
            calendarUnavailable = false;
            DateOnly date = currentDate.AddDays(1);
            while (date <= periodEnd)
            {
                CalendarLookupResult lookup = this.calendar.GetSession(this.instrumentToken, date);
                if (lookup.Status == CalendarLookupStatus.Unavailable)
                {
                    calendarUnavailable = true;
                    return false;
                }

                if (lookup.Status == CalendarLookupStatus.Found)
                {
                    return false;
                }

                date = date.AddDays(1);
            }

            return true;
        }

        private void AddOutput(string timeframeCode, CompletedCandle candle,
            List<CompletedTargetCandle> outputs)
        {
            string identity = BuildTargetIdentity(this.instrumentToken, timeframeCode, candle, this.calendar.Revision);
            if (!this.emittedTargetIdentities.Add(identity))
            {
                return;
            }

            outputs.Add(new CompletedTargetCandle(identity, timeframeCode, candle, this.calendar.Revision));
        }

        private void ResetIntradaySession(DateOnly date)
        {
            this.intradaySessionDate = date;
            this.lastIntradayClose = null;
            this.intradaySessionInvalid = false;
            this.intradayBuckets.Clear();
        }

        private ClosedCandleProcessingResult Result(ClosedCandleProcessingStatus status, string diagnostic,
            List<CompletedTargetCandle> outputs)
        {
            return new ClosedCandleProcessingResult(status, diagnostic, this.calendar.Revision, outputs.AsReadOnly());
        }

        private static DateTimeOffset GetIntradayTargetStart(DateTimeOffset sourceStart, TimeSpan duration,
            TradingSession session)
        {
            long elapsedTicks = (sourceStart - session.OpenedAt).Ticks;
            long boundaryTicks = elapsedTicks - (elapsedTicks % duration.Ticks);
            return MarketTimestamp.FromDateTime(session.OpenedAt.AddTicks(boundaryTicks).UtcDateTime);
        }

        private static DateOnly GetPeriodStart(DateOnly date, TimeframeKind kind)
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

            throw new ArgumentException("Only weekly and monthly targets have calendar periods.", nameof(kind));
        }

        private static DateOnly GetPeriodEnd(DateOnly periodStart, TimeframeKind kind)
        {
            if (kind == TimeframeKind.Weekly)
            {
                return periodStart.AddDays(6);
            }

            if (kind == TimeframeKind.Monthly)
            {
                return periodStart.AddMonths(1).AddDays(-1);
            }

            throw new ArgumentException("Only weekly and monthly targets have calendar periods.", nameof(kind));
        }

        private static string BuildSourceIdentity(string streamType, CompletedCandle candle)
        {
            return candle.Candle.InstrumentToken.ToString(CultureInfo.InvariantCulture) + "|" + streamType + "|"
                + candle.Candle.OpenedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                + candle.ClosedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildTargetIdentity(int instrumentToken, string timeframeCode, CompletedCandle candle,
            string calendarRevision)
        {
            return instrumentToken.ToString(CultureInfo.InvariantCulture) + "|" + timeframeCode + "|"
                + candle.Candle.OpenedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                + candle.ClosedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                + calendarRevision;
        }
    }
}
