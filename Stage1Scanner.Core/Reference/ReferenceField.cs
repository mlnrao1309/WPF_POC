namespace Stage1Scanner.Core.Market;

public enum ReferenceField
{
    Open,
    High,
    Low,
    Close,
    BarOpenTime,
    BarCloseTime,

    /// <summary>
    /// Number of closed bars since the reference bar. 0 on the capture bar
    /// itself, 1 on the next closed bar, etc. Enables "N+1 candles later"
    /// style rules.
    /// </summary>
    AgeInBars,
    /// <summary>
    /// The custom calculated value captured when the trigger fired.
    /// </summary>
    Value
}