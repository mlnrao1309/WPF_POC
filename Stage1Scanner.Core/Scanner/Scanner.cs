using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Rules;
using Stage1Scanner.Core.Structures;

namespace Stage1Scanner.Core.ScannerModel;

public sealed class Scanner
{
    private int _runInFlight;

    public Scanner(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Scanner name is required.", nameof(name))
            : name;

        BullishInstruments = new InstrumentWrapper(TrendDirection.Bullish);
        BearishInstruments = new InstrumentWrapper(TrendDirection.Bearish);
        RangeBoundInstruments = new InstrumentWrapper(TrendDirection.RangeBound);
        TransitionInstruments = new InstrumentWrapper(TrendDirection.Transition);
    }

    public string Name { get; }

    public bool TimeFrameInheritable { get; init; }
    public Timeframe? RootTimeFrame { get; init; }

    /// <summary>
    /// Pine-derived helper implementations can be plugged in without
    /// changing Rule/Scanner evaluation. Rules remain the source of truth
    /// for indicator and Traditional Pivot requirements.
    /// </summary>
    public IList<ICriticalLevelHelper> CriticalLevelHelpers { get; } =
        new List<ICriticalLevelHelper>();

    /// <summary>
    /// Stateful reference producers. Each captures the OHLC of the first
    /// closed bar where its trigger evaluated True, and exposes the
    /// captured bar via ReferenceOperand.
    /// </summary>
    public IList<ReferenceProducer> ReferenceProducers { get; } =
        new List<ReferenceProducer>();

    public RuleSet BullishConditions { get; init; } = new(RuleSetOperator.And);
    public RuleSet BearishConditions { get; init; } = new(RuleSetOperator.And);
    public RuleSet RangeBoundConditions { get; init; } = new(RuleSetOperator.And);
    public RuleSet TransitionConditions { get; init; } = new(RuleSetOperator.And);

    public IList<Instruments> Instruments { get; } = new List<Instruments>();

    public InstrumentWrapper BullishInstruments { get; private set; }
    public InstrumentWrapper BearishInstruments { get; private set; }
    public InstrumentWrapper RangeBoundInstruments { get; private set; }
    public InstrumentWrapper TransitionInstruments { get; private set; }

    public IReadOnlyList<ScannerConflict> DirectionalConflicts { get; private set; } =
        Array.Empty<ScannerConflict>();

    public IReadOnlyList<ScannerInsufficientData> InsufficientData { get; private set; } =
        Array.Empty<ScannerInsufficientData>();

    public IReadOnlyList<ScannerFailure> Failures { get; private set; } =
        Array.Empty<ScannerFailure>();

    public IReadOnlyList<InstrumentEvidence> Evidence { get; private set; } =
        Array.Empty<InstrumentEvidence>();

    /// <summary>
    /// When true, every significant Scanner action is traced via
    /// <see cref="Traced"/> and <see cref="System.Diagnostics.Trace"/>.
    /// Default is false; tracing has no effect on classification outcomes.
    /// </summary>
    public bool EnableTrace { get; set; } = false;

    public event EventHandler<ScannerTraceEventArgs>? Traced;

    public event EventHandler<InstrumentWrapperEventArgs>? BullishInstrumentsPopulated;
    public event EventHandler<InstrumentWrapperEventArgs>? BearishInstrumentsPopulated;
    public event EventHandler<InstrumentWrapperEventArgs>? RangeBoundInstrumentsPopulated;
    public event EventHandler<InstrumentWrapperEventArgs>? TransitionInstrumentsPopulated;

    public ScannerRequirements GetRequirements()
        => ScannerRequirementAnalyzer.GetRequirements(this);

    public void ValidateConfiguration()
    {
        if (TimeFrameInheritable && !RootTimeFrame.HasValue)
        {
            throw new InvalidOperationException(
                "TimeFrameInheritable is enabled but RootTimeFrame is not set.");
        }
    }

    public Task ExecuteConditionsAsync(
        CancellationToken cancellationToken = default)
        => ExecuteConditionsAsync(
            evaluationTime: null,
            cancellationToken);

    /// <summary>
    /// Evaluates all four RuleSets for every configured instrument.
    /// The Scanner only classifies. Entry/Exit/SL emission is layered on
    /// top of the evidence this run produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> Runs on a dedicated long-running thread. Population
    /// events fire on the awaiting caller's continuation context.
    /// </para>
    /// <para>
    /// <b>Immutability (SCN-CFG-003).</b> Instruments, CriticalLevelHelpers
    /// and ReferenceProducers are snapshotted at the start of the run.
    /// </para>
    /// <para>
    /// <b>Concurrency.</b> Concurrent runs on the same instance are rejected.
    /// </para>
    /// <para>
    /// <b>Failure isolation (SCN-NFR-003).</b> Per-instrument failures are
    /// recorded in <see cref="Failures"/>; a single failing instrument does
    /// not abort the run.
    /// </para>
    /// </remarks>
    public async Task ExecuteConditionsAsync(
        DateTimeOffset? evaluationTime,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _runInFlight, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A Scanner run is already in progress on this instance. " +
                "Await the previous ExecuteConditionsAsync call before starting another.");
        }

        try
        {
            ValidateConfiguration();
            _ = GetRequirements();

            var instrumentsSnapshot = Instruments.ToArray();
            var helpersSnapshot = CriticalLevelHelpers.ToArray();
            var referenceProducersSnapshot = ReferenceProducers.ToArray();

            var bullish = new List<Instruments>();
            var bearish = new List<Instruments>();
            var rangeBound = new List<Instruments>();
            var transition = new List<Instruments>();
            var conflicts = new List<ScannerConflict>();
            var insufficientData = new List<ScannerInsufficientData>();
            var failures = new List<ScannerFailure>();
            var evidence = new List<InstrumentEvidence>();

            await Task.Factory.StartNew(
                () => RunEvaluation(
                    instrumentsSnapshot,
                    helpersSnapshot,
                    referenceProducersSnapshot,
                    evaluationTime,
                    cancellationToken,
                    bullish,
                    bearish,
                    rangeBound,
                    transition,
                    conflicts,
                    insufficientData,
                    failures,
                    evidence),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            BullishInstruments = new InstrumentWrapper(
                TrendDirection.Bullish,
                bullish,
                evidence.Where(e => e.Classification == ScannerReasonIds.ClassificationBullish));

            BearishInstruments = new InstrumentWrapper(
                TrendDirection.Bearish,
                bearish,
                evidence.Where(e => e.Classification == ScannerReasonIds.ClassificationBearish));

            RangeBoundInstruments = new InstrumentWrapper(
                TrendDirection.RangeBound,
                rangeBound,
                evidence.Where(e => e.Classification == ScannerReasonIds.ClassificationRangeBound));

            TransitionInstruments = new InstrumentWrapper(
                TrendDirection.Transition,
                transition,
                evidence.Where(e => e.Classification == ScannerReasonIds.ClassificationTransition));

            DirectionalConflicts = conflicts.ToArray();
            InsufficientData = insufficientData.ToArray();
            Failures = failures.ToArray();
            Evidence = evidence.ToArray();

            RaisePopulated(BullishInstrumentsPopulated, BullishInstruments);
            RaisePopulated(BearishInstrumentsPopulated, BearishInstruments);
            RaisePopulated(RangeBoundInstrumentsPopulated, RangeBoundInstruments);
            RaisePopulated(TransitionInstrumentsPopulated, TransitionInstruments);
        }
        finally
        {
            Interlocked.Exchange(ref _runInFlight, 0);
        }
    }

    private void RunEvaluation(
        Instruments[] instrumentsSnapshot,
        ICriticalLevelHelper[] helpersSnapshot,
        ReferenceProducer[] referenceProducersSnapshot,
        DateTimeOffset? evaluationTime,
        CancellationToken cancellationToken,
        List<Instruments> bullish,
        List<Instruments> bearish,
        List<Instruments> rangeBound,
        List<Instruments> transition,
        List<ScannerConflict> conflicts,
        List<ScannerInsufficientData> insufficientData,
        List<ScannerFailure> failures,
        List<InstrumentEvidence> evidence)
    {
        foreach (var instruments in instrumentsSnapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset at;
            try
            {
                at = evaluationTime
                     ?? LatestClosedTime(instruments)
                     ?? throw new InvalidOperationException(
                         $"No candles are available for {instruments.Symbol}.");
            }
            catch (Exception ex)
            {
                failures.Add(new ScannerFailure(
                    instruments,
                    evaluationTime ?? default,
                    ScannerReasonIds.NoCandleData,
                    ex.Message));
                continue;
            }

            try
            {
                foreach (var helper in helpersSnapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    helper.Prepare(instruments, at);
                }

                cancellationToken.ThrowIfCancellationRequested();

                var context = new EvaluationContext(
                    instruments,
                    at,
                    TimeFrameInheritable,
                    RootTimeFrame)
                {
                    Trace = EnableTrace
                        ? message => TraceRule($"{instruments.Symbol}: {message}")
                        : null
                };

                foreach (var producer in referenceProducersSnapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    producer.Prepare(instruments, context);
                }

                var structuralReasons = EvaluateStructuralEvents(instruments, context, at);

                var bullishResult = BullishConditions.Evaluate(context);
                cancellationToken.ThrowIfCancellationRequested();

                var bearishResult = BearishConditions.Evaluate(context);
                cancellationToken.ThrowIfCancellationRequested();

                var rangeResult = RangeBoundConditions.Evaluate(context);
                cancellationToken.ThrowIfCancellationRequested();

                var transitionResult = TransitionConditions.Evaluate(context);
                cancellationToken.ThrowIfCancellationRequested();

                var directionalConflict =
                    bullishResult.Truth == RuleTruth.True &&
                    bearishResult.Truth == RuleTruth.True;

                if (directionalConflict)
                {
                    conflicts.Add(new ScannerConflict(
                        instruments, at, ScannerReasonIds.BullBearConflict));

                    Trace(ScannerTraceCategories.Conflict, at,
                        $"{instruments.Symbol}: BULL_BEAR_CONFLICT — suppressed from both wrappers.");
                }
                else if (bullishResult.Truth == RuleTruth.True)
                {
                    bullish.Add(instruments);
                    evidence.Add(BuildEvidence(
                        instruments,
                        ScannerReasonIds.ClassificationBullish,
                        at,
                        ScannerReasonIds.BullishMatch,
                        bullishResult,
                        structuralReasons));

                    Trace(ScannerTraceCategories.Classification, at,
                        $"{instruments.Symbol}: BULLISH matched.");
                }
                else if (bearishResult.Truth == RuleTruth.True)
                {
                    bearish.Add(instruments);
                    evidence.Add(BuildEvidence(
                        instruments,
                        ScannerReasonIds.ClassificationBearish,
                        at,
                        ScannerReasonIds.BearishMatch,
                        bearishResult,
                        structuralReasons));

                    Trace(ScannerTraceCategories.Classification, at,
                        $"{instruments.Symbol}: BEARISH matched.");
                }

                if (rangeResult.Truth == RuleTruth.True)
                {
                    rangeBound.Add(instruments);
                    evidence.Add(BuildEvidence(
                        instruments,
                        ScannerReasonIds.ClassificationRangeBound,
                        at,
                        ScannerReasonIds.RangeBoundMatch,
                        rangeResult,
                        structuralReasons));

                    Trace(ScannerTraceCategories.Classification, at,
                        $"{instruments.Symbol}: RANGEBOUND matched.");
                }

                if (transitionResult.Truth == RuleTruth.True)
                {
                    transition.Add(instruments);
                    evidence.Add(BuildEvidence(
                        instruments,
                        ScannerReasonIds.ClassificationTransition,
                        at,
                        ScannerReasonIds.TransitionMatch,
                        transitionResult,
                        structuralReasons));

                    Trace(ScannerTraceCategories.Classification, at,
                        $"{instruments.Symbol}: TRANSITION matched.");
                }

                var unknownClassifications = new List<string>(4);

                if (bullishResult.Truth == RuleTruth.Unknown)
                {
                    unknownClassifications.Add(ScannerReasonIds.ClassificationBullish);
                }
                if (bearishResult.Truth == RuleTruth.Unknown)
                {
                    unknownClassifications.Add(ScannerReasonIds.ClassificationBearish);
                }
                if (rangeResult.Truth == RuleTruth.Unknown)
                {
                    unknownClassifications.Add(ScannerReasonIds.ClassificationRangeBound);
                }
                if (transitionResult.Truth == RuleTruth.Unknown)
                {
                    unknownClassifications.Add(ScannerReasonIds.ClassificationTransition);
                }

                if (unknownClassifications.Count > 0)
                {
                    insufficientData.Add(new ScannerInsufficientData(
                        instruments, at, unknownClassifications));

                    Trace(ScannerTraceCategories.InsufficientData, at,
                        $"{instruments.Symbol}: Unknown for "
                        + string.Join(", ", unknownClassifications) + ".");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new ScannerFailure(
                    instruments, at, ScannerReasonIds.EvaluationFailed, ex.Message));

                Trace(ScannerTraceCategories.Failure, at,
                    $"{instruments.Symbol}: {ScannerReasonIds.EvaluationFailed} — {ex.Message}");
            }
        }
    }

    private static InstrumentEvidence BuildEvidence(
        Instruments instrument,
        string classification,
        DateTimeOffset at,
        string matchReasonId,
        RuleEvaluation result,
        IReadOnlyList<string> structuralReasons)
    {
        var paths = new List<string>();
        var operands = new List<EvidenceOperand>();

        Flatten(result, paths, operands);

        var reasonIds = new List<string> { matchReasonId };
        reasonIds.AddRange(structuralReasons);

        return new InstrumentEvidence(
            instrument, classification, at, reasonIds, paths, operands);
    }

    private static void Flatten(
        RuleEvaluation evaluation,
        List<string> paths,
        List<EvidenceOperand> operands)
    {
        paths.Add($"{evaluation.Reason}:{evaluation.Truth}");

        AddOperand(evaluation.LeftOperand, operands);
        AddOperand(evaluation.RightOperand, operands);

        if (evaluation.Children is null)
        {
            return;
        }

        foreach (var child in evaluation.Children)
        {
            Flatten(child, paths, operands);
        }
    }

    private static void AddOperand(
        OperandValue? operand,
        List<EvidenceOperand> operands)
    {
        if (!operand.HasValue)
        {
            return;
        }

        var op = operand.Value;

        operands.Add(new EvidenceOperand(
            op.Name ?? string.Empty,
            op.TimeFrame?.ToString(),
            op.Offset,
            op.HasValue ? op.Value : null));
    }

    private static List<string> EvaluateStructuralEvents(
        Instruments instruments,
        EvaluationContext context,
        DateTimeOffset at)
    {
        var reasons = new List<string>(4);

        foreach (var level in instruments.Structures.Levels.ToArray())
        {
            if (!instruments.TryGetCandles(level.Timeframe, out var candles))
            {
                continue;
            }

            var idx = context.ResolveClosedIndex(level.Timeframe, at);
            if (idx < 0)
            {
                continue;
            }

            var candle = candles[idx];

            AddStructuralReason(reasons, instruments.EvaluateBreak(level.Id, candle).Type);
            AddStructuralReason(reasons, instruments.EvaluateAnchor(level.Id, candle).Type);
        }

        foreach (var zone in instruments.Structures.Zones.ToArray())
        {
            if (!instruments.TryGetCandles(zone.Timeframe, out var candles))
            {
                continue;
            }

            var idx = context.ResolveClosedIndex(zone.Timeframe, at);
            if (idx < 0)
            {
                continue;
            }

            var candle = candles[idx];

            AddStructuralReason(reasons, instruments.EvaluateBreak(zone.Id, candle).Type);
            AddStructuralReason(reasons, instruments.EvaluateAnchor(zone.Id, candle).Type);
        }

        return reasons;
    }

    private static void AddStructuralReason(List<string> reasons, StructuralEventType type)
    {
        var id = type switch
        {
            StructuralEventType.AnchorCreated       => ScannerReasonIds.AnchorCreated,
            StructuralEventType.BullishContinuation => ScannerReasonIds.AnchorContinuation,
            StructuralEventType.BearishContinuation => ScannerReasonIds.AnchorContinuation,
            StructuralEventType.BullishReversal     => ScannerReasonIds.AnchorReversal,
            StructuralEventType.BearishReversal     => ScannerReasonIds.AnchorReversal,
            _                                       => null
        };

        if (id is not null && !reasons.Contains(id))
        {
            reasons.Add(id);
        }
    }

    private void RaisePopulated(
        EventHandler<InstrumentWrapperEventArgs>? handler,
        InstrumentWrapper wrapper)
        => handler?.Invoke(this, new InstrumentWrapperEventArgs(wrapper));

    private void Trace(string category, DateTimeOffset at, string message)
    {
        if (!EnableTrace)
        {
            return;
        }

        Traced?.Invoke(this, new ScannerTraceEventArgs(at, category, message));
        System.Diagnostics.Trace.WriteLine(
            $"[Scanner:{Name}][{category}] {message}");
    }

    private void TraceRule(string message)
    {
        if (!EnableTrace)
        {
            return;
        }

        Traced?.Invoke(this, new ScannerTraceEventArgs(
            DateTimeOffset.UtcNow,
            ScannerTraceCategories.Rule,
            message));
    }

    private static DateTimeOffset? LatestClosedTime(Instruments instruments)
    {
        DateTimeOffset? latest = null;

        foreach (var timeframe in instruments.AvailableTimeframes)
        {
            var candles = instruments.GetCandles(timeframe);

            if (candles.Count == 0)
            {
                continue;
            }

            var candidate = candles[^1].CloseTime;
            latest = !latest.HasValue || candidate > latest.Value
                ? candidate
                : latest;
        }

        return latest;
    }
}