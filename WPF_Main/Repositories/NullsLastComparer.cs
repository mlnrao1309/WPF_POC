namespace AlgoTrading.Screener.Dsl;

/// <summary>
/// Compares heterogeneous series values (decimal, bool, string) and always
/// sorts nulls to the end regardless of direction. Indicator warm-up produces
/// nulls, and putting them first by default is almost never what users want.
/// </summary>
internal sealed class NullsLastComparer : IComparer<object?>
{
    public static readonly NullsLastComparer Instance = new();

    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return  1;   // nulls last
        if (y is null) return -1;

        if (TryNum(x, out var dx) && TryNum(y, out var dy))
            return dx.CompareTo(dy);

        if (x is bool bx && y is bool by)
            return bx.CompareTo(by);

        if (x is string sx && y is string sy)
            return string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);

        return string.Compare(x.ToString(), y.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNum(object o, out decimal d)
    {
        switch (o)
        {
            case decimal v: d = v;         return true;
            case int    v: d = v;          return true;
            case long   v: d = v;          return true;
            case short  v: d = v;          return true;
            case byte   v: d = v;          return true;
            case double v: d = (decimal)v; return true;
            case float  v: d = (decimal)v; return true;
            default:       d = 0m;         return false;
        }
    }
}