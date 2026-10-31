namespace AlgoTrading.RuleEditor.Models;

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

    // reference
    public string? ReferenceId { get; set; }
    public string? ReferenceField { get; set; } = "High";

    // indicator
    public string? Indicator { get; set; }
    public string? Output { get; set; }
    public EditorExpression? Source { get; set; }
    public Dictionary<string, decimal> Params { get; set; } = new();

    // Count indicator
    public EditorNode? Predicate { get; set; }
    public int Lookback { get; set; } = 5;
    public bool Streak { get; set; }

    // calculation
    public string? CalcOp { get; set; }
    public EditorExpression? CalcLeft { get; set; }
    public EditorExpression? CalcRight { get; set; }
}