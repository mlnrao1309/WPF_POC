namespace AlgoTrading.RuleEditor.Models;

public sealed class EditorReferenceBlock
{
    public string Name { get; set; } = "RefBlock";
    public string Timeframe { get; set; } = "15m";
    public EditorNode Trigger { get; set; } = new() { Kind = "group", Logic = "All", Enabled = true };
    public EditorNode? ClearOn { get; set; }
    public List<EditorReferenceCapture> Captures { get; set; } = new();
}

public sealed class EditorReferenceCapture
{
    public string Id { get; set; } = "";
    public EditorExpression Value { get; set; } = new() { Kind = "field", Field = "Close" };
}