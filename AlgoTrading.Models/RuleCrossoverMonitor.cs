namespace AlgoTrading.Models.Rules
{
    /// <summary>
    /// Persistent, in-memory event monitoring for one rule and instrument. Reuse this monitor as completed data grows.
    /// Process scans new completed candles; ordinary Evaluate remains side-effect free.
    /// </summary>
    public sealed class RuleCrossoverMonitor
    {
        private readonly BoundRule rule;
        private readonly RuleDefinition definition;
        private readonly IReadOnlyList<CrossoverSubscription> subscriptions;
        private readonly DateTimeOffset? startAfter;
        private readonly Dictionary<string, DateTimeOffset> processedThrough = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private readonly List<PendingCrossover> pendingCrossovers = new List<PendingCrossover>();
        private readonly object processingLock = new object ();
        private int? instrumentToken;
        private bool processing;
        private DateTimeOffset? lastObservation;
        public event EventHandler<CrossoverEventArgs>? CrossoverDetected;
        public event EventHandler<CrossoverEventArgs>? CrossoverNextCandleAvailable;
        private readonly IndicatorCalculationVersion calculationVersion;
        private readonly string dataRevision;
        private readonly IndicatorCalculationCache cache;

        internal RuleCrossoverMonitor(BoundRule rule, RuleDefinition definition, DateTimeOffset? startAfter,
            IndicatorCalculationVersion version, string dataRevision, IndicatorCalculationCache cache)
        {
            this.rule = rule;
            this.definition = definition;
            this.startAfter = startAfter;
            this.calculationVersion = version;
            this.dataRevision = dataRevision;
            this.cache = cache;
            subscriptions = new CrossoverDiscovery().Discover(definition);
        }

        /// <summary>
        /// Emits each new enabled crossover once, regardless of the overall rule match. Future rows are excluded by asOf.
        /// On first use all supplied completed history is processed unless startAfter was specified when creating the monitor.
        /// Retain complete history, including pending events' next candles, in subsequent immutable snapshots.
        /// </summary>
        public RuleEvaluation Process(RuleMarketData data, DateTimeOffset asOf)
        {
            ArgumentNullException.ThrowIfNull(data);
            BeginProcessing(data, asOf);
            try
            {
                BoundRuleExecution execution = rule.BindData(data, this.cache, this.dataRevision, this.calculationVersion);
                RuleEvaluation evaluation = execution.Evaluate(asOf);
                CrossoverSnapshotBuilder builder = new CrossoverSnapshotBuilder(data, execution);
                List<CrossoverOccurrence> detected = new List<CrossoverOccurrence>();
                List<CrossoverOccurrence> completed = new List<CrossoverOccurrence>();
                CollectNextCandles(data, asOf, builder, completed);
                foreach (CrossoverSubscription subscription in subscriptions)
                {
                    CollectCrossovers(data, asOf, execution, builder, subscription, detected);
                }

                detected.Sort(CrossoverOccurrenceComparer.Instance);
                completed.Sort(CrossoverOccurrenceComparer.Instance);
                lastObservation = asOf;
                // State is committed before user code runs. Callback errors do not cause duplicate event replay.
                List<Exception> failures = new List<Exception>();
                Publish(CrossoverDetected, detected, failures);
                Publish(CrossoverNextCandleAvailable, completed, failures);
                if (failures.Count > 0)
                {
                    throw new AggregateException("One or more crossover event subscribers failed.", failures);
                }

                return evaluation;
            }
            finally
            {
                lock (processingLock)
                {
                    processing = false;
                }
            }
        }

        private void BeginProcessing(RuleMarketData data, DateTimeOffset asOf)
        {
            lock (processingLock)
            {
                if (processing)
                {
                    throw new InvalidOperationException("Process calls on a crossover monitor must not overlap or re-enter from an event handler.");
                }

                if (instrumentToken.HasValue && instrumentToken.Value != data.InstrumentToken)
                {
                    throw new ArgumentException("Use a separate crossover monitor for each instrument.", nameof(data));
                }

                if (lastObservation.HasValue && asOf < lastObservation.Value)
                {
                    throw new ArgumentException("Process timestamps must move forward. Use a new monitor to replay earlier history.", nameof(asOf));
                }

                instrumentToken = data.InstrumentToken;
                processing = true;
            }
        }

        private void CollectCrossovers(RuleMarketData data, DateTimeOffset asOf, BoundRuleExecution execution, CrossoverSnapshotBuilder builder, CrossoverSubscription subscription, List<CrossoverOccurrence> detected)
        {
            CompletedCandle[] candles = data.GetSeries(subscription.Timeframe);
            int finalIndex = RuleMarketData.FindCompletedIndex(candles, asOf);
            DateTimeOffset? watermark = startAfter;
            if (processedThrough.TryGetValue(subscription.Path, out DateTimeOffset processed))
            {
                watermark = processed;
            }

            int firstIndex = 0;
            if (watermark.HasValue)
            {
                firstIndex = RuleMarketData.FindCompletedIndex(candles, watermark.Value) + 1;
            }

            for (int candleIndex = firstIndex; candleIndex <= finalIndex; candleIndex++)
            {
                DateTimeOffset timestamp = candles[candleIndex].ClosedAt;
                RuleStatus status = execution.SampleCondition(subscription.Condition, timestamp, subscription.Timeframe);
                if (status != RuleStatus.Matched || candleIndex < 1)
                {
                    continue;
                }

                CrossoverCandleSnapshot? next = null;
                if (candleIndex < finalIndex)
                {
                    next = builder.Capture(subscription, candles[candleIndex + 1]);
                }

                CrossoverOccurrence occurrence = new CrossoverOccurrence(Guid.NewGuid(), definition.Name, subscription.Path, RuleLanguage.Describe(subscription.Condition), data.InstrumentToken, subscription.Timeframe, subscription.Condition.Operator, timestamp, asOf, builder.Capture(subscription, candles[candleIndex - 1]), builder.Capture(subscription, candles[candleIndex]), next, execution.Evaluate(timestamp));
                detected.Add(occurrence);
                if (next == null)
                {
                    pendingCrossovers.Add(new PendingCrossover(subscription, occurrence));
                }
            }

            if (finalIndex >= firstIndex)
            {
                processedThrough[subscription.Path] = candles[finalIndex].ClosedAt;
            }
        }

        private void CollectNextCandles(RuleMarketData data, DateTimeOffset asOf, CrossoverSnapshotBuilder builder, List<CrossoverOccurrence> completed)
        {
            for (int pendingIndex = pendingCrossovers.Count - 1; pendingIndex >= 0; pendingIndex--)
            {
                PendingCrossover pending = pendingCrossovers[pendingIndex];
                CompletedCandle[] candles = data.GetSeries(pending.Subscription.Timeframe);
                int nextIndex = RuleMarketData.FindCompletedIndex(candles, pending.Occurrence.OccurredAt) + 1;
                if (nextIndex >= candles.Length || candles[nextIndex].ClosedAt > asOf)
                {
                    continue;
                }

                completed.Add(pending.Occurrence.WithNextCandle(asOf, builder.Capture(pending.Subscription, candles[nextIndex])));
                pendingCrossovers.RemoveAt(pendingIndex);
            }
        }

        private void Publish(EventHandler<CrossoverEventArgs>? handlers, IReadOnlyList<CrossoverOccurrence> occurrences, List<Exception> failures)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (CrossoverOccurrence occurrence in occurrences)
            {
                CrossoverEventArgs arguments = new CrossoverEventArgs(occurrence);
                foreach (Delegate subscriber in handlers.GetInvocationList())
                {
                    try
                    {
                        ((EventHandler<CrossoverEventArgs>)subscriber)(this, arguments);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
        }
    }
}

