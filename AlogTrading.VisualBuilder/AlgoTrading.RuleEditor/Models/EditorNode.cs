namespace AlgoTrading.RuleEditor.Models;

/// <summary>
/// Editor-side, mutable condition node: either a "group" (All/Any with
/// children) or a "condition" (left / operator / right).
/// </summary>
public sealed class EditorNode
{
    public string Kind { get; set; } = "group";
    public bool Enabled { get; set; } = true;

    // group only
    public string Logic { get; set; } = "All";
    public string? Name { get; set; }
    public List<EditorNode> Children { get; set; } = new();

    // condition only
    public string? Comment { get; set; }
    public EditorExpression Left { get; set; } = new();
    public string Operator { get; set; } = "GreaterThan";
    public EditorExpression Right { get; set; } = new();
}