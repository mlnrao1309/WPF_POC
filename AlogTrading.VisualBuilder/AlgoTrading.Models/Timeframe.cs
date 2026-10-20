using System.Globalization;

namespace AlgoTrading.Models;

public readonly struct Timeframe : IEquatable<Timeframe>, IComparable<Timeframe>
{
    public TimeframeUnit Unit { get; }
    public int Count { get; }

    public Timeframe(TimeframeUnit unit, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must be > 0.");
        if ((unit == TimeframeUnit.D || unit == TimeframeUnit.W || unit == TimeframeUnit.M) && count != 1)
            throw new ArgumentException($"Multi-{unit} timeframes are not supported in v1.");
        Unit = unit;
        Count = count;
    }

    public static Timeframe Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("Timeframe string is empty.");

        s = s.Trim();

        // Bare D/W/M
        if (s.Length == 1 && (s == "D" || s == "W" || s == "M"))
            return new Timeframe(s switch { "D" => TimeframeUnit.D, "W" => TimeframeUnit.W, _ => TimeframeUnit.M }, 1);

        // <int><unit> where unit in {m,H}
        if (s.EndsWith("m", StringComparison.Ordinal))
        {
            var n = s[..^1];
            if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var c) || c <= 0)
                throw new FormatException($"Invalid minute timeframe: {s}");
            return new Timeframe(TimeframeUnit.m, c);
        }
        if (s.EndsWith("H", StringComparison.Ordinal))
        {
            var n = s[..^1];
            if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var c) || c <= 0)
                throw new FormatException($"Invalid hour timeframe: {s}");
            return new Timeframe(TimeframeUnit.H, c);
        }

        throw new FormatException($"Unsupported timeframe: {s}");
    }

    public static bool TryParse(string s, out Timeframe tf)
    {
        try { tf = Parse(s); return true; }
        catch { tf = default; return false; }
    }

    public override string ToString() => Unit switch
    {
        TimeframeUnit.m => $"{Count}m",
        TimeframeUnit.H => $"{Count}H",
        TimeframeUnit.D => "D",
        TimeframeUnit.W => "W",
        TimeframeUnit.M => "M",
        _ => throw new InvalidOperationException()
    };

    // Total minutes for m/H; throws for D/W/M.
    public int ToMinutes() => Unit switch
    {
        TimeframeUnit.m => Count,
        TimeframeUnit.H => Count * 60,
        _ => throw new InvalidOperationException($"ToMinutes is not defined for {Unit}.")
    };

    public bool Equals(Timeframe other) => Unit == other.Unit && Count == other.Count;
    public override bool Equals(object? obj) => obj is Timeframe t && Equals(t);
    public override int GetHashCode() => HashCode.Combine(Unit, Count);

    public int CompareTo(Timeframe other)
    {
        // m/H are comparable in minutes; D/W/M sort after all intraday.
        bool aIntra = Unit == TimeframeUnit.m || Unit == TimeframeUnit.H;
        bool bIntra = other.Unit == TimeframeUnit.m || other.Unit == TimeframeUnit.H;

        if (aIntra && bIntra) return ToMinutes().CompareTo(other.ToMinutes());
        if (aIntra) return -1;
        if (bIntra) return 1;
        return ((int)Unit).CompareTo((int)other.Unit);
    }

    public static bool operator ==(Timeframe a, Timeframe b) => a.Equals(b);
    public static bool operator !=(Timeframe a, Timeframe b) => !a.Equals(b);
}