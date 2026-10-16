using System.Runtime.CompilerServices;
using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Indicators;

internal static class IndicatorCache
{
    private static readonly ConditionalWeakTable<Candles, Dictionary<string, object>> Cache = new();

    public static T GetOrCreate<T>(Candles candles, string key, Func<T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentNullException.ThrowIfNull(factory);

        var perSeries = Cache.GetOrCreateValue(candles);

        lock (perSeries)
        {
            if (perSeries.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }

            var value = factory();
            perSeries[key] = value;
            return value;
        }
    }

    public static void Clear(Candles candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        Cache.Remove(candles);
    }
}
