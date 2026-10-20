using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Models;

/// <summary>
/// Editor session state wrapping a RuleDefinition. Condition trees are held
/// inside Rule (loaded from JSON, preserved on save). Overview fields are
/// mutated directly on Rule by OverviewTab.
/// </summary>
public sealed class EditorStrategy
{
    public RuleDefinition Rule { get; set; } = null!;
    public string? CurrentFilePath { get; set; }
    public bool IsDirty { get; set; }

    public bool IsTuned => Rule.InstrumentToken.HasValue && Rule.InstrumentToken.Value != 0;
}