namespace AlgoTrading.Screener.Dsl;

/// <summary>
/// Tracks the grammar version of the screener DSL. Bump CurrentVersion
/// whenever the syntax changes in a way that would break previously-saved
/// queries. SavedQuery stores the version it was created under so the
/// loader can warn the user (or attempt migration) when opening older
/// queries after a grammar change.
/// </summary>
public static class DslGrammar
{
    /// <summary>
    /// 1 — initial version. Includes: from/let/where/select/orderby/take,
    ///     per bar / per symbol, offsets, crossedabove/below, between,
    ///     keyword comparisons, all functions shipped through Wave 2.
    /// </summary>
    public const int CurrentVersion = 1;
}