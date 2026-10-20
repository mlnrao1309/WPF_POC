using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

/// <summary>
/// Evaluates a Condition or ConditionGroup at a base bar index. Completed
/// bars only. Handles all 12 operators, including crossovers which consult
/// the previous bar via offset+1 on each side (Â§2.5).
/// </summary>
public sealed class ConditionEvaluator
{
    private readonly ExpressionEvaluator _expr;

    public ConditionEvaluator(ExpressionEvaluator expr) { _expr = expr; }

    public bool Evaluate(Node node, Timeframe defaultTf, int baseIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            ConditionGroup g => EvaluateGroup(g, defaultTf, baseIndex),
            Condition c => EvaluateCondition(c, defaultTf, baseIndex),
            _ => false
        };
    }

    private static bool IsNodeEnabled(Node node) => node switch
    {
        Condition c => c.Enabled,
        ConditionGroup g => g.Enabled,
        _ => true
    };

    public bool EvaluateGroup(ConditionGroup group, Timeframe defaultTf, int baseIndex)
    {
        if (!group.Enabled) return true;

        if (group.Logic == GroupLogic.All)
        {
            foreach (var child in group.Children)
            {
                if (!IsNodeEnabled(child)) continue;
                if (!Evaluate(child, defaultTf, baseIndex)) return false;
            }
            // Empty group of All â†’ true (Â§2.7.2).
            return true;
        }

        // Any
        foreach (var child in group.Children)
        {
            if (!IsNodeEnabled(child)) continue;
            if (Evaluate(child, defaultTf, baseIndex)) return true;
        }
        // Empty Any â†’ false (Â§2.7.2).
        return false;
    }

    public bool EvaluateCondition(Condition cond, Timeframe defaultTf, int baseIndex)
    {
        if (!cond.Enabled) return false;
        var op = cond.Operator;

        if (op is Operator.CrossedAbove or Operator.CrossedBelow)
        {
            if (!_expr.TryEvaluate(cond.Left, defaultTf, baseIndex, 0, out var ln)) return false;
            if (!_expr.TryEvaluate(cond.Right, defaultTf, baseIndex, 0, out var rn)) return false;
            if (!_expr.TryEvaluate(cond.Left, defaultTf, baseIndex, 1, out var lp)) return false;
            if (!_expr.TryEvaluate(cond.Right, defaultTf, baseIndex, 1, out var rp)) return false;

            return op == Operator.CrossedAbove
                ? ln > rn && lp <= rp
                : ln < rn && lp >= rp;
        }

        if (!_expr.TryEvaluate(cond.Left, defaultTf, baseIndex, 0, out var l)) return false;
        if (!_expr.TryEvaluate(cond.Right, defaultTf, baseIndex, 0, out var r)) return false;

        return op switch
        {
            Operator.GreaterThan    => l > r,
            Operator.LessThan       => l < r,
            Operator.GreaterOrEqual => l >= r,
            Operator.LessOrEqual    => l <= r,
            Operator.Equal          => l == r,
            Operator.NotEqual       => l != r,
            // Opened*/Closed* are unary aliases (Â§2.5). Left side already
            // constrained to Open or Close by the validator.
            Operator.OpenedAbove    => l > r,
            Operator.OpenedBelow    => l < r,
            Operator.ClosedAbove    => l > r,
            Operator.ClosedBelow    => l < r,
            _ => false
        };
    }
}