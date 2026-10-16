namespace AlgoTrading.Screener.Dsl;

/// <summary>
/// The runner executes a DSL query against the current database and returns
/// matching rows. Kept behind an interface so tests and future consumers
/// (CLI, background scanner) can substitute a fake.
/// </summary>
public interface IScreenerRunner
{
    Task<IReadOnlyList<ScreenerRow>> RunAsync(
        string source,
        ScreenerRequest request,
        CancellationToken cancellationToken = default);
}