using System;
using System.Collections.Generic;
using AlgoTrading.Data;
using AlgoTrading.Indicators;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal sealed class RuleCompiler
{
    private readonly TimeframeAligner _aligner;
    private readonly Timeframe _evalTf;
    private readonly IReadOnlyDictionary<string, decimal> _params;
    private readonly Dictionary<IndicatorExpression, decimal[]> _indCache = new();

    public RuleCompiler(TimeframeAligner aligner, Timeframe evalTf, IReadOnlyDictionary<string, decimal> parameters)
    {
        _aligner = aligner; _evalTf = evalTf; _params = parameters;
    }

    public CompiledNode CompileGroup(ConditionGroup? group)
    {
        if (group is null) return new CompiledGroup(GroupLogic.All, Array.Empty<CompiledNode>());
        if (!group.Enabled) return new CompiledGroup(group.Logic, Array.Empty<CompiledNode>());
        return CompileGroupInternal(group, _evalTf);
    }

    private CompiledNode CompileGroupInternal(ConditionGroup g, Timeframe parent)
    {
        var compiled = new List<CompiledNode>();
        if (g.Children is not null)
        {
            var ch = (IReadOnlyList<Node>)g.Children;
            for (int i = 0; i < ch.Count; i++)
            {
                if (ch[i] is ConditionGroup cg)
                {
                    if (!cg.Enabled) continue;
                    compiled.Add(CompileGroupInternal(cg, parent));
                }
                else if (ch[i] is Condition c)
                {
                    if (!c.Enabled) continue;
                    compiled.Add(CompileCondition(c, parent));
                }
            }
        }
        return new CompiledGroup(g.Logic, compiled.ToArray());
    }

    private CompiledCondition CompileCondition(Condition c, Timeframe parent)
    {
        var l = CompileExpr(c.Left, parent);
        var r = CompileExpr(c.Right, parent);
        return new CompiledCondition(l, c.Operator, r);
    }

    private CompiledExpression CompileExpr(ValueExpression e, Timeframe parent)
    {
        return e switch
        {
            NumberExpression n    => new CompiledConstant(n.Value),
            ParameterExpression p => new CompiledConstant(ResolveParam(p.Name)),
            FieldExpression f     => CompileField(f, parent),
            IndicatorExpression i => CompileIndicator(i, parent),
            CalculationExpression c => new CompiledCalculation(c.Op, CompileExpr(c.Left, parent), CompileExpr(c.Right, parent)),
            _ => throw new InvalidOperationException($"Unsupported expression: {e?.GetType().Name ?? "null"}"),
        };
    }

    private decimal ResolveParam(string name)
    {
        if (!_params.TryGetValue(name, out var v)) throw new InvalidOperationException($"Parameter '{name}' not found.");
        return v;
    }

    private CompiledExpression CompileField(FieldExpression f, Timeframe parent)
    {
        Timeframe tf = f.Timeframe ?? parent;
        var s = _aligner.Series(tf);
        var col = SelectColumn(s, f.Field);
        return new CompiledField(col, _aligner.GetMap(tf), f.Offset);
    }

    private CompiledExpression CompileIndicator(IndicatorExpression ind, Timeframe parent)
    {
        Timeframe tf = ind.Timeframe ?? parent;
        var values = GetOrCompute(ind, tf);
        return new CompiledIndicatorSeries(values, _aligner.GetMap(tf), ind.Offset);
    }

    private static ReadOnlyMemory<decimal> SelectColumn(IBarSeries s, Field f) => f switch
    {
        Field.Open   => s.Open,
        Field.High   => s.High,
        Field.Low    => s.Low,
        Field.Close  => s.Close,
        Field.Volume => s.Volume,
        Field.OI     => s.OI,
        _ => throw new InvalidOperationException($"Unsupported field: {f}"),
    };

    private decimal[] GetOrCompute(IndicatorExpression ind, Timeframe tf)
    {
        if (_indCache.TryGetValue(ind, out var cached)) return cached;

        var desc = IndicatorRegistry.Get(ind.Indicator);
        var series = _aligner.Series(tf);
        int n = series.Count;

        ReadOnlyMemory<decimal> srcMem = default;
        if (desc.Source == SourceKind.Single)
        {
            srcMem = ResolveSource(ind.Source, tf) ?? throw new InvalidOperationException("Indicator source required.");
        }
        else if (desc.Source == SourceKind.Optional)
        {
            srcMem = ind.Source is null ? series.Close : (ResolveSource(ind.Source, tf) ?? series.Close);
        }

        int pCount = desc.Parameters.Count;
        var pv = new decimal[pCount];
        for (int i = 0; i < pCount; i++)
        {
            var p = desc.Parameters[i];
            pv[i] = (ind.Params is not null && ind.Params.TryGetValue(p.Name, out var v)) ? v : p.Default;
        }

        var nullableOuts = new decimal?[IndicatorDispatch.MaxOutputs][];
        for (int k = 0; k < IndicatorDispatch.MaxOutputs; k++) nullableOuts[k] = new decimal?[n];

        IndicatorDispatch.Execute(ind.Indicator, series, srcMem.Span, pv, nullableOuts, out int actualCount);

        string outName = ind.Output ?? desc.Outputs[0];
        int chosen = 0;
        for (int k = 0; k < desc.Outputs.Count; k++)
            if (string.Equals(desc.Outputs[k], outName, StringComparison.Ordinal)) { chosen = k; break; }
        if (chosen >= actualCount) throw new InvalidOperationException($"Output '{outName}' out of range.");

        var src = nullableOuts[chosen];
        var dense = new decimal[n];
        for (int i = 0; i < n; i++) dense[i] = src[i] ?? Dec.NaN;

        _indCache[ind] = dense;
        return dense;
    }

    private ReadOnlyMemory<decimal>? ResolveSource(ValueExpression? e, Timeframe parent)
    {
        if (e is null) return null;
        if (e is FieldExpression f)
        {
            if (f.Offset != 0) throw new InvalidOperationException("Non-zero offset on indicator source not supported in v1.");
            Timeframe tf = f.Timeframe ?? parent;
            return SelectColumn(_aligner.Series(tf), f.Field);
        }
        if (e is IndicatorExpression nested)
        {
            if (nested.Offset != 0) throw new InvalidOperationException("Non-zero offset on nested indicator source not supported in v1.");
            Timeframe tf = nested.Timeframe ?? parent;
            return GetOrCompute(nested, tf);
        }
        throw new InvalidOperationException("Indicator source must be field or indicator.");
    }
}