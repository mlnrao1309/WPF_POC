using AlgoTrading.Screener.Dsl;
using static AlgoTrading.Screener.Repositories.ScreenerRepository;

namespace AlgoTrading.Screener.Repositories;

public interface IScreenerRepository
{
    Task<IReadOnlyList<InstrumentRef>> GetUniverseAsync(
        string timeframe,
        DateTime fromInclusive,
        DateTime toExclusive,
        string? segment,
        string? exchange,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, IReadOnlyList<Bar>>> GetBarsBulkAsync(
        IReadOnlyCollection<long> instrumentTokens,
        string timeframe,
        DateTime fromInclusive,
        DateTime toExclusive,
        int? maxBarsPerInstrument = null,
        CancellationToken cancellationToken = default);

    Task<FilterOptions> LoadFilterOptionsAsync();
}

public sealed record InstrumentRef(
    long InstrumentToken,
    string TradingSymbol,
    string? Segment,
    string? Exchange);

/// <summary>
/// One OHLCV bar with a nested <see cref="Pivots"/> snapshot.
/// Constructed from the database with seven arguments; pivot fields are
/// attached later via <c>with { Pivots = ... }</c> in the repository
/// enrichment step, so the seven-argument construction site remains valid.
/// </summary>
public sealed record Bar
{
    public DateTime TimeStamp { get; init; }
    public decimal  Open      { get; init; }
    public decimal  High      { get; init; }
    public decimal  Low       { get; init; }
    public decimal  Close     { get; init; }
    public long?     Volume    { get; init; }
    public decimal? OI        { get; init; }
    public Pivots   Pivots    { get; init; } = Pivots.Empty;

    public Bar(
        DateTime timeStamp,
        decimal open, decimal high, decimal low, decimal close,
        long? volume, decimal? oi,
        Pivots? pivots = null)
    {
        TimeStamp = timeStamp;
        Open      = open;
        High      = high;
        Low       = low;
        Close     = close;
        Volume    = volume;
        OI        = oi;
        Pivots    = pivots ?? Pivots.Empty;
    }
}
