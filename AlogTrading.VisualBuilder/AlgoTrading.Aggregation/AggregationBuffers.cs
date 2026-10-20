using System.Runtime.CompilerServices;

namespace AlgoTrading.Aggregation;

/// <summary>
/// Fixed-size column buffer for an aggregated bar series.
/// A ref struct so the container itself never reaches the heap;
/// only the seven backing arrays are allocated (exactly once,
/// exact size). For count == 0 all references collapse to the
/// shared Array.Empty singletons.
/// </summary>
internal readonly ref struct AggregationBuffers
{
    public readonly DateTime[] Dt;
    public readonly decimal[] Op;
    public readonly decimal[] Hi;
    public readonly decimal[] Lo;
    public readonly decimal[] Cl;
    public readonly decimal[] Vo;
    public readonly decimal[] Oi;

    public int Length { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Dt.Length; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AggregationBuffers(int count)
    {
        if (count == 0)
        {
            Dt = Array.Empty<DateTime>();
            Op = Array.Empty<decimal>();
            Hi = Array.Empty<decimal>();
            Lo = Array.Empty<decimal>();
            Cl = Array.Empty<decimal>();
            Vo = Array.Empty<decimal>();
            Oi = Array.Empty<decimal>();
        }
        else
        {
            Dt = new DateTime[count];
            Op = new decimal[count];
            Hi = new decimal[count];
            Lo = new decimal[count];
            Cl = new decimal[count];
            Vo = new decimal[count];
            Oi = new decimal[count];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColumnarBarSeries ToSeries(uint token, string symbol, Models.Timeframe tf) =>
        new ColumnarBarSeries(token, symbol, tf, Op, Hi, Lo, Cl, Vo, Oi, Dt);
}