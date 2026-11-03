using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Runtime;

public sealed class ConditionEvaluator
{
    private readonly ExpressionEvaluator _expr;
    public ConditionEvaluator(ExpressionEvaluator expr) { _expr = expr; }

    public bool Evaluate(Node node, Timeframe defaultTf, int baseIndex, DateTime evaluationTimeUtc,
        List<EvaluationTraceRecord>? traces = null, string? jsonPath = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            ConditionGroup g => EvaluateGroup(g, defaultTf, baseIndex, evaluationTimeUtc, traces, jsonPath),
            Condition c => EvaluateCondition(c, defaultTf, baseIndex, evaluationTimeUtc, traces, jsonPath),
            _ => false
        };
    }

    private static bool IsNodeEnabled(Node node) => node switch
    {
        Condition c => c.Enabled,
        ConditionGroup g => g.Enabled,
        _ => true
    };

    public bool EvaluateGroup(ConditionGroup group, Timeframe defaultTf, int baseIndex, DateTime evaluationTimeUtc,
        List<EvaluationTraceRecord>? traces = null, string? jsonPath = null)
    {
        if (!group.Enabled) return true;
        if (group.Logic == GroupLogic.All)
        {
            for (int i = 0; i < group.Children.Count; i++)
            {
                if (!IsNodeEnabled(group.Children[i])) continue;
                var p = jsonPath is null ? null : $"{jsonPath}/children/{i}";
                if (!Evaluate(group.Children[i], defaultTf, baseIndex, evaluationTimeUtc, traces, p)) return false;
            }
            return true;
        }
        for (int i = 0; i < group.Children.Count; i++)
        {
            if (!IsNodeEnabled(group.Children[i])) continue;
            var p = jsonPath is null ? null : $"{jsonPath}/children/{i}";
            if (Evaluate(group.Children[i], defaultTf, baseIndex, evaluationTimeUtc, traces, p)) return true;
        }
        return false;
    }

    public bool EvaluateCondition(Condition cond, Timeframe defaultTf, int baseIndex, DateTime evaluationTimeUtc,
        List<EvaluationTraceRecord>? traces = null, string? jsonPath = null)
        => TryEvaluateCondition(cond, defaultTf, baseIndex, evaluationTimeUtc, out var match, traces, jsonPath) && match;

    public bool TryEvaluateCondition(Condition cond, Timeframe defaultTf, int baseIndex, DateTime evaluationTimeUtc,
        out bool match, List<EvaluationTraceRecord>? traces = null, string? jsonPath = null)
    {
        match = false;
        if (!cond.Enabled) return true;
        var op = cond.Operator;
        var nodePath = jsonPath ?? cond.Comment ?? "";

        // M2: Capture reference revision info before evaluation
        ReferenceValue? leftRef = null, rightRef = null;
        if (cond.Left is ReferenceExpression lre && _expr.TryGetReferenceInfo(lre.Id, out var lv)) leftRef = lv;
        if (cond.Right is ReferenceExpression rre && _expr.TryGetReferenceInfo(rre.Id, out var rv)) rightRef = rv;

        var left = _expr.TryEvaluate(cond.Left, defaultTf, baseIndex, 0, out var lVal, out var lTime);
        var right = _expr.TryEvaluate(cond.Right, defaultTf, baseIndex, 0, out var rVal, out var rTime);

        if (op is Operator.CrossedAbove or Operator.CrossedBelow)
        {
            var leftPrev = _expr.TryEvaluate(cond.Left, defaultTf, baseIndex, 1, out var lPrevVal, out _);
            var rightPrev = _expr.TryEvaluate(cond.Right, defaultTf, baseIndex, 1, out var rPrevVal, out _);

            if (!left || !right || !leftPrev || !rightPrev)
            {
                match = false;
                if (traces != null)
                {
                    traces.Add(new EvaluationTraceRecord
                    {
                        EvaluationTimeUtc = evaluationTimeUtc,
                        NodePath = nodePath,
                        IsEnabled = true,
                        Operator = op.ToString(),
                        Result = "Unknown",
                        LeftValue = left ? lVal : null,
                        RightValue = right ? rVal : null,
                        PreviousLeftValue = leftPrev ? lPrevVal : null,
                        PreviousRightValue = rightPrev ? rPrevVal : null,
                        LeftAvailableAt = lTime,
                        RightAvailableAt = rTime,
                        LeftReferenceId = leftRef?.CapturedValue.HasValue == true ? ((ReferenceExpression)cond.Left).Id : null,
                        LeftReferenceCapturedBarIndex = leftRef?.CapturedBarIndex,
                        LeftReferenceCapturedValue = leftRef?.CapturedValue,
                        RightReferenceId = rightRef?.CapturedValue.HasValue == true ? ((ReferenceExpression)cond.Right).Id : null,
                        RightReferenceCapturedBarIndex = rightRef?.CapturedBarIndex,
                        RightReferenceCapturedValue = rightRef?.CapturedValue
                    });
                }
                return true;
            }

            match = op == Operator.CrossedAbove
                ? lVal > rVal && lPrevVal <= rPrevVal
                : lVal < rVal && lPrevVal >= rPrevVal;

            if (traces != null)
            {
                traces.Add(new EvaluationTraceRecord
                {
                    EvaluationTimeUtc = evaluationTimeUtc,
                    NodePath = nodePath,
                    IsEnabled = true,
                    Operator = op.ToString(),
                    Result = match ? "True" : "False",
                    LeftValue = lVal,
                    RightValue = rVal,
                    PreviousLeftValue = lPrevVal,
                    PreviousRightValue = rPrevVal,
                    LeftAvailableAt = lTime,
                    RightAvailableAt = rTime,
                    LeftReferenceId = leftRef != null ? ((ReferenceExpression)cond.Left).Id : null,
                    LeftReferenceCapturedBarIndex = leftRef?.CapturedBarIndex,
                    LeftReferenceCapturedValue = leftRef?.CapturedValue,
                    RightReferenceId = rightRef != null ? ((ReferenceExpression)cond.Right).Id : null,
                    RightReferenceCapturedBarIndex = rightRef?.CapturedBarIndex,
                    RightReferenceCapturedValue = rightRef?.CapturedValue
                });
            }
            return true;
        }

        if (!left || !right)
        {
            match = false;
            if (traces != null)
            {
                traces.Add(new EvaluationTraceRecord
                {
                    EvaluationTimeUtc = evaluationTimeUtc,
                    NodePath = nodePath,
                    IsEnabled = true,
                    Operator = op.ToString(),
                    Result = "Unknown",
                    LeftValue = left ? lVal : null,
                    RightValue = right ? rVal : null,
                    LeftAvailableAt = lTime,
                    RightAvailableAt = rTime,
                    LeftReferenceId = leftRef != null ? ((ReferenceExpression)cond.Left).Id : null,
                    LeftReferenceCapturedBarIndex = leftRef?.CapturedBarIndex,
                    LeftReferenceCapturedValue = leftRef?.CapturedValue,
                    RightReferenceId = rightRef != null ? ((ReferenceExpression)cond.Right).Id : null,
                    RightReferenceCapturedBarIndex = rightRef?.CapturedBarIndex,
                    RightReferenceCapturedValue = rightRef?.CapturedValue
                });
            }
            return true;
        }

        match = op switch
        {
            Operator.GreaterThan => lVal > rVal,
            Operator.LessThan => lVal < rVal,
            Operator.GreaterOrEqual => lVal >= rVal,
            Operator.LessOrEqual => lVal <= rVal,
            Operator.Equal => lVal == rVal,
            Operator.NotEqual => lVal != rVal,
            Operator.OpenedAbove => lVal > rVal,
            Operator.OpenedBelow => lVal < rVal,
            Operator.ClosedAbove => lVal > rVal,
            Operator.ClosedBelow => lVal < rVal,
            _ => false
        };

        if (traces != null)
        {
            traces.Add(new EvaluationTraceRecord
            {
                EvaluationTimeUtc = evaluationTimeUtc,
                NodePath = nodePath,
                IsEnabled = true,
                Operator = op.ToString(),
                Result = match ? "True" : "False",
                LeftValue = lVal,
                RightValue = rVal,
                LeftAvailableAt = lTime,
                RightAvailableAt = rTime,
                LeftReferenceId = leftRef != null ? ((ReferenceExpression)cond.Left).Id : null,
                LeftReferenceCapturedBarIndex = leftRef?.CapturedBarIndex,
                LeftReferenceCapturedValue = leftRef?.CapturedValue,
                RightReferenceId = rightRef != null ? ((ReferenceExpression)cond.Right).Id : null,
                RightReferenceCapturedBarIndex = rightRef?.CapturedBarIndex,
                RightReferenceCapturedValue = rightRef?.CapturedValue
            });
        }

        return Enum.IsDefined(op);
    }
}