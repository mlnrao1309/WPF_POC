namespace Stage1Scanner.Core.ScannerModel;

public static class ScannerTraceCategories
{
    public const string Run = "Run";
    public const string Instrument = "Instrument";
    public const string Classification = "Classification";
    public const string Conflict = "Conflict";
    public const string Failure = "Failure";
    public const string InsufficientData = "InsufficientData";
    public const string Rule = "Rule";
    public const string Aggregate = "Aggregate";
}

public sealed class ScannerTraceEventArgs : EventArgs
{
    public ScannerTraceEventArgs(
        DateTimeOffset at,
        string category,
        string message)
    {
        At = at;
        Category = category;
        Message = message;
    }

    public DateTimeOffset At { get; }
    public string Category { get; }
    public string Message { get; }

    public override string ToString()
        => $"[{At:O}][{Category}] {Message}";
}