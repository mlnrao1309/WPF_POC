namespace AlgoTrading.Screener.Dsl;

internal static class QueryHistoryRequirements
{
    public static bool TryGetFiniteBars(QueryNode query, out int bars)
    {
        ArgumentNullException.ThrowIfNull(query);
        bars = 1;
        if (query.PerBar) return false;

        var lets = query.Lets.ToDictionary(x => x.Name, x => x.Value,
            StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (LetClause let in query.Lets)
                bars = Math.Max(bars, GetBars(let.Value, lets, visiting));
            if (query.Where is not null)
                bars = Math.Max(bars, GetBars(query.Where, lets, visiting));
            foreach (OrderItem item in query.OrderBy)
                bars = Math.Max(bars, GetBars(item.Value, lets, visiting));
            foreach (SelectItem item in query.Select)
                bars = Math.Max(bars, GetBars(item.Value, lets, visiting));
            return true;
        }
        catch (UnsupportedRequirementException)
        {
            bars = 0;
            return false;
        }
        catch (OverflowException)
        {
            bars = 0;
            return false;
        }
    }

    private static int GetBars(Expr expression, IReadOnlyDictionary<string, Expr> lets,
        HashSet<string> visiting)
    {
        return expression switch
        {
            Num or Str or BoolLit or Member => 1,
            Ident identifier => GetIdentifierBars(identifier, lets, visiting),
            Index index => checked(GetBars(index.Target, lets, visiting) + GetOffset(index.Offset)),
            Unary unary => GetBars(unary.Operand, lets, visiting),
            Binary binary => GetBinaryBars(binary, lets, visiting),
            Between between => Math.Max(GetBars(between.Value, lets, visiting),
                Math.Max(GetBars(between.Lower, lets, visiting), GetBars(between.Upper, lets, visiting))),
            Call call => GetCallBars(call, lets, visiting),
            _ => throw new UnsupportedRequirementException()
        };
    }

    private static int GetIdentifierBars(Ident identifier, IReadOnlyDictionary<string, Expr> lets,
        HashSet<string> visiting)
    {
        if (!lets.TryGetValue(identifier.Name, out Expr? value)) return 1;
        if (!visiting.Add(identifier.Name)) throw new UnsupportedRequirementException();
        try
        {
            return GetBars(value, lets, visiting);
        }
        finally
        {
            visiting.Remove(identifier.Name);
        }
    }

    private static int GetBinaryBars(Binary binary, IReadOnlyDictionary<string, Expr> lets,
        HashSet<string> visiting)
    {
        int required = Math.Max(GetBars(binary.Left, lets, visiting),
            GetBars(binary.Right, lets, visiting));
        return binary.Op is "crossedabove" or "crossedbelow" or "touchedabove" or "touchedbelow"
            ? checked(required + 1)
            : required;
    }

    private static int GetCallBars(Call call, IReadOnlyDictionary<string, Expr> lets,
        HashSet<string> visiting)
    {
        string name = call.Name.ToUpperInvariant();
        if (name == "SMA")
        {
            if (call.Args.Count != 2) throw new UnsupportedRequirementException();
            int period = GetPositiveInteger(call.Args[1]);
            return checked(GetBars(call.Args[0], lets, visiting) + period - 1);
        }

        if (name is "ABS" or "SQRT" or "LOG" or "EXP" or "ROUND" or "FLOOR" or "CEILING")
        {
            if (call.Args.Count != 1) throw new UnsupportedRequirementException();
            return GetBars(call.Args[0], lets, visiting);
        }

        throw new UnsupportedRequirementException();
    }

    private static int GetOffset(Expr expression)
    {
        if (expression is not Num number || number.Value < 0 || number.Value != decimal.Truncate(number.Value)
            || number.Value > int.MaxValue)
            throw new UnsupportedRequirementException();
        return decimal.ToInt32(number.Value);
    }

    private static int GetPositiveInteger(Expr expression)
    {
        int value = GetOffset(expression);
        if (value == 0) throw new UnsupportedRequirementException();
        return value;
    }

    private sealed class UnsupportedRequirementException : Exception;
}
