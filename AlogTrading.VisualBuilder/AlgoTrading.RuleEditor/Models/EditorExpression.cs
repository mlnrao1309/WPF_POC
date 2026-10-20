namespace AlgoTrading.RuleEditor.Models;

/// <summary>
/// Editor-side, mutable value expression. Fields not relevant to the current
/// Kind are ignored during serialization but preserved in memory, so switching
/// kinds back and forth does not lose sub-editors.
/// </summary>
public sealed class EditorExpression
{
    public string Kind { get; set; } = "field";

    // field + indicator
    public int Offset { get; set; }
    public string? Timeframe { get; set; }

    // field
    public string? Field { get; set; } = "Close";

    // number
    public decimal Number { get; set; }

    // parameter
    public string? ParameterName { get; set; }

    // indicator
    public string? Indicator { get; set; }
    public string? Output { get; set; }
    public EditorExpression? Source { get; set; }
    public Dictionary<string, decimal> Params { get; set; } = new();

    // calculation
    public string? CalcOp { get; set; }
    public EditorExpression? CalcLeft { get; set; }
    public EditorExpression? CalcRight { get; set; }
}