using System;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

internal abstract class CompiledNode { public abstract bool Evaluate(int baseIndex); }

internal sealed class CompiledCondition : CompiledNode
{
    private readonly CompiledExpression _l, _r;
    private readonly Operator _op;
    public CompiledCondition(CompiledExpression l, Operator op, CompiledExpression r) { _l = l; _op = op; _r = r; }

    public override bool Evaluate(int b)
    {
        if (_op == Operator.CrossedAbove)
        {
            decimal l0 = _l.Evaluate(b, 0), r0 = _r.Evaluate(b, 0);
            if (Dec.IsNaN(l0) || Dec.IsNaN(r0)) return false;
            decimal l1 = _l.Evaluate(b, 1), r1 = _r.Evaluate(b, 1);
            if (Dec.IsNaN(l1) || Dec.IsNaN(r1)) return false;
            return l0 > r0 && l1 <= r1;
        }
        if (_op == Operator.CrossedBelow)
        {
            decimal l0 = _l.Evaluate(b, 0), r0 = _r.Evaluate(b, 0);
            if (Dec.IsNaN(l0) || Dec.IsNaN(r0)) return false;
            decimal l1 = _l.Evaluate(b, 1), r1 = _r.Evaluate(b, 1);
            if (Dec.IsNaN(l1) || Dec.IsNaN(r1)) return false;
            return l0 < r0 && l1 >= r1;
        }
        decimal l = _l.Evaluate(b, 0); if (Dec.IsNaN(l)) return false;
        decimal r = _r.Evaluate(b, 0); if (Dec.IsNaN(r)) return false;
        return _op switch
        {
            Operator.GreaterThan    => l > r,
            Operator.LessThan       => l < r,
            Operator.GreaterOrEqual => l >= r,
            Operator.LessOrEqual    => l <= r,
            Operator.Equal          => l == r,
            Operator.NotEqual       => l != r,
            Operator.OpenedAbove    => l > r,
            Operator.OpenedBelow    => l < r,
            Operator.ClosedAbove    => l > r,
            Operator.ClosedBelow    => l < r,
            _ => false,
        };
    }
}

internal sealed class CompiledGroup : CompiledNode
{
    private readonly GroupLogic _logic;
    private readonly CompiledNode[] _kids;
    public CompiledGroup(GroupLogic logic, CompiledNode[] kids) { _logic = logic; _kids = kids; }

    public override bool Evaluate(int b)
    {
        if (_kids.Length == 0) return _logic == GroupLogic.All;
        if (_logic == GroupLogic.All)
        {
            for (int i = 0; i < _kids.Length; i++) if (!_kids[i].Evaluate(b)) return false;
            return true;
        }
        for (int i = 0; i < _kids.Length; i++) if (_kids[i].Evaluate(b)) return true;
        return false;
    }
}