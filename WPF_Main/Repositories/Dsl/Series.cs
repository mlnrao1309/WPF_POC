namespace AlgoTrading.Screener.Dsl;

/// <summary>
/// A per-bar series. Values are decimal?, bool?, or string?. Null means
/// "no value" (indicator warm-up, missing bar, or an offset that fell off).
/// </summary>
public sealed class Series
{
    public object?[] Values { get; }
    public int Length => Values.Length;
    public object? Last => Values.Length == 0 ? null : Values[^1];

    public Series(object?[] values) => Values = values;

    // ---- factories ----
    public static Series Num(IEnumerable<decimal?> v) => new(v.Cast<object?>().ToArray());
    public static Series Num(decimal?[] v)            => new(v.Cast<object?>().ToArray());
    public static Series Num(IEnumerable<long?> v)    => new(v.Select(x => (decimal?)x).Cast<object?>().ToArray());
    public static Series Num(long?[] v)               => Num((IEnumerable<long?>)v);
    public static Series Bool(IEnumerable<bool?> v)   => new(v.Cast<object?>().ToArray());
    public static Series Bool(bool?[] v)              => new(v.Cast<object?>().ToArray());
    public static Series Text(string?[] v)            => new(v.Cast<object?>().ToArray());
    public static Series Constant(object? v, int n)   => new(Enumerable.Repeat(v, n).ToArray());
    public static Series Empty(int n)                 => new(new object?[n]);

    // ---- accessors ----
    public decimal? AsNum(int i) => Values[i] switch
    {
        decimal d => d,
        null      => null,
        bool b    => b ? 1m : 0m,
        int x     => x,
        long x    => x,
        short x   => x,
        byte x    => x,
        _         => throw new InvalidOperationException(
                        $"Not numeric at index {i}: {Values[i]?.GetType().Name}")
    };

    public bool? AsBool(int i) => Values[i] switch
    {
        bool b    => b,
        null      => null,
        decimal d => d != 0m,
        _         => throw new InvalidOperationException(
                        $"Not boolean at index {i}: {Values[i]?.GetType().Name}")
    };

    public string? AsText(int i) => Values[i] as string;

    public decimal? LastNum() => Last switch
    {
        decimal d => d,
        int i     => i,
        long l    => l,
        short s   => s,
        byte b    => b,
        null      => null,
        _         => throw new InvalidOperationException(
                        $"Last value is not numeric ({Last!.GetType().Name}).")
    };

    public bool? LastBool() => Last switch
    {
        bool b    => b,
        null      => null,
        decimal d => d != 0m,
        _         => throw new InvalidOperationException(
                        $"Last value is not boolean ({Last!.GetType().Name}).")
    };

    /// <summary>Shift forward k bars: result[i] = this[i - k]. Negative k forbidden.</summary>
    public Series Shift(int k)
    {
        if (k < 0) throw new InvalidOperationException(
            "Lookahead offsets (negative) are not allowed.");
        if (k == 0) return this;

        var r = new object?[Length];
        for (int i = 0; i < Length; i++)
        {
            int j = i - k;
            r[i] = j >= 0 ? Values[j] : null;
        }
        return new Series(r);
    }
    public static Series Num(IEnumerable<decimal> v)
    => new(v.Select(x => (decimal?)x).Cast<object?>().ToArray());

    public static Series Num(decimal[] v)
        => Num((IEnumerable<decimal>)v);
}