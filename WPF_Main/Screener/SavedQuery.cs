using AlgoTrading.Screener.Dsl;

namespace AlgoTrading.Screener.Storage;

/// <summary>
/// A stored query — either user-saved or a shipped template.
/// Immutable so it's safe to hand around the UI thread without copies.
/// </summary>
public sealed record SavedQuery(
    string Id,
    string Name,
    string Query,
    string Category,
    string? Description,
    DateTime CreatedUtc,
    DateTime? LastRunUtc,
    int? LastRunRowCount,
    int GrammarVersion = DslGrammar.CurrentVersion)   // new
{
    public static SavedQuery NewSaved(string name, string query, string? description = null)
        => new(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Query: query,
            Category: "Saved",
            Description: description,
            CreatedUtc: DateTime.UtcNow,
            LastRunUtc: null,
            LastRunRowCount: null);
    // GrammarVersion defaults to DslGrammar.CurrentVersion
}