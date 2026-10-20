using System;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal abstract class CompiledExpression
{
    public abstract decimal Evaluate(int baseIndex, int extraOffset = 0);
}

internal sealed class CompiledConstant : CompiledExpression
{
    private readonly decimal _v;
    public CompiledConstant(decimal v) { _v = v; }
    public override decimal Evaluate(int b, int o = 0) => _v;
}

internal sealed class CompiledField : CompiledExpression
{
    private readonly ReadOnlyMemory<decimal> _values;
    private readonly int[] _map;
    private readonly int _off;
    public CompiledField(ReadOnlyMemory<decimal> values, int[] map, int off) { _values = values; _map = map; _off = off; }
    public override decimal Evaluate(int b, int o = 0)
    {
        int j = _map[b]; if (j < 0) return Dec.NaN;
        int k = j - _off - o; if (k < 0) return Dec.NaN;
        return _values.Span[k];
    }
}

internal sealed class CompiledIndicatorSeries : CompiledExpression
{
    private readonly decimal[] _s;
    private readonly int[] _map;
    private readonly int _off;
    public CompiledIndicatorSeries(decimal[] s, int[] map, int off) { _s = s; _map = map; _off = off; }
    public override decimal Evaluate(int b, int o = 0)
    {
        int j = _map[b]; if (j < 0) return Dec.NaN;
        int k = j - _off - o; if (k < 0) return Dec.NaN;
        return _s[k];
    }
}

internal sealed class CompiledCalculation : CompiledExpression
{
    private readonly CalculationOp _op;
    private readonly CompiledExpression _l, _r;
    public CompiledCalculation(CalculationOp op, CompiledExpression l, CompiledExpression r) { _op = op; _l = l; _r = r; }
    public override decimal Evaluate(int b, int o = 0)
    {
        decimal l = _l.Evaluate(b, o); if (Dec.IsNaN(l)) return Dec.NaN;
        decimal r = _r.Evaluate(b, o); if (Dec.IsNaN(r)) return Dec.NaN;
        try
        {
            return _op switch
            {
                CalculationOp.Add      => l + r,
                CalculationOp.Subtract => l - r,
                CalculationOp.Multiply => l * r,
                CalculationOp.Divide   => r == 0m ? Dec.NaN : l / r,
                CalculationOp.Modulo   => r == 0m ? Dec.NaN : l % r,
                CalculationOp.Min      => Math.Min(l, r),
                CalculationOp.Max      => Math.Max(l, r),
                _ => Dec.NaN,
            };
        }
        catch (OverflowException) { return Dec.NaN; }
        catch (DivideByZeroException) { return Dec.NaN; }
    }
}