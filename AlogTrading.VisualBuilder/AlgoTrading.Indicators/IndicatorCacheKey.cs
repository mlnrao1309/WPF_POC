using System;
using AlgoTrading.Models;

namespace AlgoTrading.Indicators;

public readonly struct IndicatorCacheKey : IEquatable<IndicatorCacheKey>
{
    public uint InstrumentToken { get; }
    public Timeframe Timeframe { get; }
    public IndicatorKind Kind { get; }
    public string Output { get; }
    public string ParamsKey { get; }
    public string SourceKey { get; }

    public IndicatorCacheKey(
        uint instrumentToken,
        Timeframe timeframe,
        IndicatorKind kind,
        string output,
        string paramsKey,
        string sourceKey)
    {
        InstrumentToken = instrumentToken;
        Timeframe = timeframe;
        Kind = kind;
        Output = output ?? string.Empty;
        ParamsKey = paramsKey ?? string.Empty;
        SourceKey = sourceKey ?? string.Empty;
    }

    public bool Equals(IndicatorCacheKey other)
        => InstrumentToken == other.InstrumentToken
        && Timeframe.Equals(other.Timeframe)
        && Kind == other.Kind
        && string.Equals(Output, other.Output, StringComparison.Ordinal)
        && string.Equals(ParamsKey, other.ParamsKey, StringComparison.Ordinal)
        && string.Equals(SourceKey, other.SourceKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is IndicatorCacheKey k && Equals(k);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(InstrumentToken);
        h.Add(Timeframe);
        h.Add((int)Kind);
        h.Add(Output);
        h.Add(ParamsKey);
        h.Add(SourceKey);
        return h.ToHashCode();
    }

    public static bool operator ==(IndicatorCacheKey a, IndicatorCacheKey b) => a.Equals(b);
    public static bool operator !=(IndicatorCacheKey a, IndicatorCacheKey b) => !a.Equals(b);
}