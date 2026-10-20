namespace AlgoTrading.RuleEditor.Models;

public sealed class EditorParameter
{
    public string Name { get; set; } = "";
    public decimal Default { get; set; }
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; }
    public decimal Step { get; set; } = 1m;
}