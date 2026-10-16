using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Series;

/// <summary>
/// Candle-aligned numeric series. Values[i] always refers to Candles[i].
/// Null represents TradingView-like 'na' / insufficient warm-up.
/// </summary>
public sealed class NumericSeries
{
    public NumericSeries(
        Candles candles,
        decimal?[] values,
        string name)
    {
        Candles = candles;
        Values = values;
        Name = name;

        if (candles.Count != values.Length)
        {
            throw new ArgumentException("Candle and value counts must match.");
        }
    }

    public Candles Candles { get; }
    public Timeframe Timeframe => Candles.Timeframe;
    public decimal?[] Values { get; }
    public string Name { get; }

    public int Count => Values.Length;

    public decimal? this[int index] => Values[index];

    public bool TryGetValue(int index, out decimal value)
    {
        value = default;

        if (index < 0 || index >= Values.Length)
        {
            return false;
        }

        if (!Values[index].HasValue)
        {
            return false;
        }

        value = Values[index]!.Value;
        return true;
    }
}

public sealed class MacdSeries
{
    public required NumericSeries Macd { get; init; }
    public required NumericSeries Signal { get; init; }
    public required NumericSeries Histogram { get; init; }
}

public sealed class BollingerBandSeries
{
    public required NumericSeries Basis { get; init; }
    public required NumericSeries Upper { get; init; }
    public required NumericSeries Lower { get; init; }
}

public enum SuperTrendDirection
{
    Bullish = 1,
    Bearish = -1
}

public sealed class StochasticSeries
{
    public required NumericSeries K { get; init; }
    public required NumericSeries D { get; init; }
}

public sealed class AdxSeries
{
    public required NumericSeries Adx { get; init; }
    public required NumericSeries PlusDi { get; init; }
    public required NumericSeries MinusDi { get; init; }
}

public sealed class KeltnerSeries
{
    public required NumericSeries Basis { get; init; }
    public required NumericSeries Upper { get; init; }
    public required NumericSeries Lower { get; init; }
}

public sealed class DonchianSeries
{
    public required NumericSeries Upper { get; init; }
    public required NumericSeries Middle { get; init; }
    public required NumericSeries Lower { get; init; }
}
