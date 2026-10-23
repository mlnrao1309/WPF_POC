namespace AlgoTrading.Screener.Dsl;

public sealed class Evaluator
{
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<Series>, int, Series>> _functions;

    private readonly IndicatorEvaluationCache? _indicatorCache;

    public Evaluator(IReadOnlyDictionary<string, Func<IReadOnlyList<Series>, int, Series>> functions,
        IndicatorEvaluationCache? indicatorCache = null)
    {
        _functions = functions;
        _indicatorCache = indicatorCache;
    }

    public Series EvalSeries(Expr e, EvalEnv env) => (Series)Eval(e, env);

    public object Eval(Expr e, EvalEnv env) => e switch
    {
        Num n      => Series.Constant(n.Value, env.Length),
        Str s      => Series.Constant(s.Value, env.Length),
        BoolLit b  => Series.Constant(b.Value, env.Length),
        Ident i    => env.Resolve(i.Name),
        Member m   => EvalMember(m, env),
        Index ix   => EvalIndex(ix, env),
        Unary u    => EvalUnary(u, env),
        Binary b   => EvalBinary(b, env),
        Between bt => EvalBetween(bt, env),
        Call c     => EvalCall(c, env),
        _          => throw new NotSupportedException(e.GetType().Name)
    };

    private Series EvalMember(Member m, EvalEnv env)
    {
        var target = Eval(m.Target, env);
        if (target is not SymbolValue sv)
            throw new InvalidOperationException(
                $"Member access '{m.Name}' requires a symbol.");

        if (!sv.Fields.TryGetValue(m.Name, out var field))
            throw new InvalidOperationException(
                $"Symbol has no field '{m.Name}'.");
        return field;
    }

    private Series EvalIndex(Index ix, EvalEnv env)
    {
        var target       = EvalSeries(ix.Target, env);
        var offsetSeries = EvalSeries(ix.Offset, env);
        int n = env.Length;

        // Fast path: constant offset across all bars.
        decimal? first   = offsetSeries.AsNum(0);
        bool isConstant  = true;
        for (int i = 1; i < n; i++)
        {
            if (offsetSeries.AsNum(i) != first) { isConstant = false; break; }
        }

        if (isConstant)
        {
            int k = (int)(first ?? 0);
            if (k < 0) throw new InvalidOperationException(
                "Lookahead offsets (negative) are not allowed.");
            return target.Shift(k);
        }

        // Dynamic per-bar offset.
        var r = new object?[n];
        for (int i = 0; i < n; i++)
        {
            var o = offsetSeries.AsNum(i);
            if (o is null) { r[i] = null; continue; }

            int k = (int)o.Value;
            if (k < 0) throw new InvalidOperationException(
                "Lookahead offsets (negative) are not allowed.");
            if (k > i) { r[i] = null; continue; }

            r[i] = target.Values[i - k];
        }
        return new Series(r);
    }

    private Series EvalUnary(Unary u, EvalEnv env)
    {
        var a = EvalSeries(u.Operand, env);
        var r = new object?[env.Length];
        for (int i = 0; i < env.Length; i++)
        {
            switch (u.Op)
            {
                case "-":
                    { var x = a.AsNum(i); r[i] = x is null ? null : -x; break; }
                case "not":
                    { var b = a.AsBool(i); r[i] = b is null ? null : !b; break; }
            }
        }
        return new Series(r);
    }

    private Series EvalBetween(Between bt, EvalEnv env)
    {
        var v  = EvalSeries(bt.Value, env);
        var lo = EvalSeries(bt.Lower, env);
        var hi = EvalSeries(bt.Upper, env);
        var r  = new object?[env.Length];

        for (int i = 0; i < env.Length; i++)
        {
            var x = v.AsNum(i); var a = lo.AsNum(i); var b = hi.AsNum(i);
            if (x is null || a is null || b is null) { r[i] = null; continue; }
            r[i] = x.Value >= a.Value && x.Value <= b.Value;
        }
        return new Series(r);
    }

    private Series EvalBinary(Binary b, EvalEnv env)
    {
        if (b.Op is "crossedabove" or "crossedbelow" or "touchedabove" or "touchedbelow")
            return EvalCross(b, env);

        var L = EvalSeries(b.Left, env);
        var R = EvalSeries(b.Right, env);
        var r = new object?[env.Length];

        bool isLogic = b.Op is "and" or "or";

        for (int i = 0; i < env.Length; i++)
        {
            if (isLogic)
            {
                var x = L.AsBool(i); var y = R.AsBool(i);
                if (x is null || y is null) { r[i] = null; continue; }
                r[i] = b.Op == "and" ? (x.Value && y.Value) : (x.Value || y.Value);
                continue;
            }

            // string equality
            if (b.Op is "==" or "!=" &&
                (L.Values[i] is string || R.Values[i] is string))
            {
                var s1 = L.Values[i] as string;
                var s2 = R.Values[i] as string;
                if (s1 is null || s2 is null) { r[i] = null; continue; }
                bool eq = string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);
                r[i] = b.Op == "==" ? eq : !eq;
                continue;
            }

            var a = L.AsNum(i); var c = R.AsNum(i);
            if (a is null || c is null) { r[i] = null; continue; }

            r[i] = b.Op switch
            {
                "+"  => a.Value + c.Value,
                "-"  => a.Value - c.Value,
                "*"  => a.Value * c.Value,
                "/"  => c.Value == 0m ? (decimal?)null : a.Value / c.Value,
                "%"  => c.Value == 0m ? (decimal?)null : a.Value % c.Value,
                "==" => (object?)(a.Value == c.Value),
                "!=" => (object?)(a.Value != c.Value),
                ">"  => (object?)(a.Value >  c.Value),
                ">=" => (object?)(a.Value >= c.Value),
                "<"  => (object?)(a.Value <  c.Value),
                "<=" => (object?)(a.Value <= c.Value),
                _    => throw new NotSupportedException($"Operator '{b.Op}'")
            };
        }
        return new Series(r);
    }

    private Series EvalCross(Binary b, EvalEnv env)
    {
        var L = EvalSeries(b.Left, env);
        var R = EvalSeries(b.Right, env);
        var r = new object?[env.Length];

        for (int i = 0; i < env.Length; i++)
        {
            if (i == 0) { r[i] = false; continue; }

            var l0 = L.AsNum(i - 1); var l1 = L.AsNum(i);
            var r0 = R.AsNum(i - 1); var r1 = R.AsNum(i);
            if (l0 is null || l1 is null || r0 is null || r1 is null) { r[i] = null; continue; }

            r[i] = b.Op switch
            {
                "crossedabove" => l1.Value > r1.Value && l0.Value <= r0.Value,
                "crossedbelow" => l1.Value < r1.Value && l0.Value >= r0.Value,
                "touchedabove" => l1.Value > r1.Value && l0.Value <= r0.Value,
                "touchedbelow" => l1.Value < r1.Value && l0.Value >= r0.Value,
                _ => (object?)null
            };
        }
        return new Series(r);
    }

    private Series EvalCall(Call c, EvalEnv env)
    {
        if (!_functions.TryGetValue(c.Name, out var fn))
            throw new InvalidOperationException($"Unknown function '{c.Name}'.");

        var args = c.Args.Select(a => EvalSeries(a, env)).ToList();
        return _indicatorCache == null ? fn(args, env.Length) : _indicatorCache.Evaluate(c, args, fn);
    }
}