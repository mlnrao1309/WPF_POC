namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Machine-readable reason identifiers used by Scanner evidence.
/// Keep in sync with the SRS §16.1 controlled reason vocabulary.
/// </summary>
public static class ScannerReasonIds
{
    // Directional / state
    public const string BullishMatch = "BULLISH_MATCH";
    public const string BearishMatch = "BEARISH_MATCH";
    public const string RangeBoundMatch = "RANGEBOUND_MATCH";
    public const string TransitionMatch = "TRANSITION_MATCH";

    // Conflicts and status
    public const string BullBearConflict = "BULL_BEAR_CONFLICT";
    public const string InsufficientData = "INSUFFICIENT_DATA";

    // Additional IDs proposed for this implementation (SRS delta required)
    public const string NoCandleData = "NO_CANDLE_DATA";
    public const string EvaluationFailed = "EVALUATION_FAILED";

    // Classification labels used inside InsufficientData records
    public const string ClassificationBullish = "BULLISH";
    public const string ClassificationBearish = "BEARISH";
    public const string ClassificationRangeBound = "RANGEBOUND";
    public const string ClassificationTransition = "TRANSITION";
    // Structural break / anchor facts (SRS §16.1).
    public const string AnchorCreated = "ANCHOR_CREATED";
    public const string AnchorContinuation = "ANCHOR_CONTINUATION";
    public const string AnchorReversal = "ANCHOR_REVERSAL";
}