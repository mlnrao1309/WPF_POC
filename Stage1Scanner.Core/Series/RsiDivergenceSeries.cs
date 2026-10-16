namespace Stage1Scanner.Core.Series;

public enum RsiDivergenceType
{
    Bullish,
    Bearish
}

/// <summary>
/// Rich RSI result. Value is Wilder RSI. Smooth is optional EMA/SMA/WMA
/// smoothing of the RSI line. Divergence is one entry per bar; non-null
/// only on the confirmation bar of a pivot-based regular divergence.
/// </summary>
public sealed class RsiSeries
{
    public required NumericSeries Value { get; init; }

    public NumericSeries? Smooth { get; init; }

    public RsiDivergenceType?[] Divergence { get; init; } =
        Array.Empty<RsiDivergenceType?>();
}