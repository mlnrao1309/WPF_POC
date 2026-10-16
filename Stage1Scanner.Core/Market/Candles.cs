using System.Collections;

namespace Stage1Scanner.Core.Market;

/// <summary>
/// A homogeneous candle series for exactly one timeframe.
/// Indicator extension methods operate on this type.
/// </summary>
public sealed class Candles : IReadOnlyList<Candle>
{
    private readonly IReadOnlyList<Candle> _items;

    public Candles(Timeframe timeframe, IEnumerable<Candle> items)
    {
        Timeframe = timeframe;
        ArgumentNullException.ThrowIfNull(items);

        var ordered = items.OrderBy(x => x.CloseTime).ToArray();

        for (var i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].CloseTime < ordered[i].OpenTime)
            {
                throw new ArgumentException(
                    "Candle CloseTime cannot be earlier than OpenTime.",
                    nameof(items));
            }

            if (i > 0 && ordered[i].CloseTime <= ordered[i - 1].CloseTime)
            {
                throw new ArgumentException(
                    "Candles must have unique increasing CloseTime values.",
                    nameof(items));
            }
        }

        _items = ordered;
    }

    public Timeframe Timeframe { get; }

    public int Count => _items.Count;

    public Candle this[int index] => _items[index];

    public IEnumerator<Candle> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
