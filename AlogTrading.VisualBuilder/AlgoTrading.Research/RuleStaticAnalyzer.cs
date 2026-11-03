using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace AlgoTrading.Research.Analysis;

public static class RuleStaticAnalyzer
{
    public static List<StaticFinding> Analyze(RuleDefinition rule)
    {
        var findings = new List<StaticFinding>();

        AnalyzeGroup(rule.Entry, "/entry", rule.EvaluationTimeframe, findings);
        if (rule.Eligibility != null) AnalyzeGroup(rule.Eligibility, "/eligibility", rule.EvaluationTimeframe, findings);
        if (rule.Protection != null) AnalyzeGroup(rule.Protection, "/protection", rule.EvaluationTimeframe, findings);
        if (rule.Exit != null) AnalyzeGroup(rule.Exit, "/exit", rule.EvaluationTimeframe, findings);
        if (rule.FullExit != null) AnalyzeGroup(rule.FullExit, "/fullExit", rule.EvaluationTimeframe, findings);

        for (int i = 0; i < rule.References.Count; i++)
        {
            var block = rule.References[i];
            var path = $"/references/{i}/trigger";
            if (block.Trigger is ConditionGroup cg)
                AnalyzeGroup(cg, path, block.Timeframe, findings);
            else if (block.Trigger is Condition c)
                AnalyzeCondition(c, path, block.Timeframe, findings);
        }

        return findings;
    }

    private static void AnalyzeGroup(ConditionGroup? group, string path, Timeframe defaultTf, List<StaticFinding> findings)
    {
        if (group == null || !group.Enabled) return;

        // RULE: SEM001 - Event-filter conflict inside an 'All' group
        if (group.Logic == GroupLogic.All)
        {
            var eventConditions = new List<(Condition Cond, string Path)>();
            var stateConditions = new List<(Condition Cond, string Path)>();

            for (int i = 0; i < group.Children.Count; i++)
            {
                var childPath = $"{path}/children/{i}";
                if (group.Children[i] is Condition c && c.Enabled)
                {
                    if (c.Operator is Operator.CrossedAbove or Operator.CrossedBelow)
                        eventConditions.Add((c, childPath));
                    else
                        stateConditions.Add((c, childPath));
                }
            }

            if (eventConditions.Count > 0 && stateConditions.Count > 0)
            {
                foreach (var ev in eventConditions)
                {
                    // FIXED: Propose the correct state operator based on the crossing direction
                    string newOp = ev.Cond.Operator == Operator.CrossedAbove ? "GreaterThan" : "LessThan";

                    findings.Add(new StaticFinding
                    {
                        Id = $"SEM001_{ev.Path}",
                        Title = "Event-filter conflict",
                        Description = $"The condition at {ev.Path} uses a crossing operator ({ev.Cond.Operator}) but is ANDed with other persistent state filters in the same 'All' group. The crossing event only lasts for one bar, so this combination may rarely trigger.",
                        Severity = FindingSeverity.HighReview,
                        Certainty = FindingCertainty.Suspected,
                        JsonPointer = ev.Path,
                        SuggestedPatches = new List<CandidatePatch>
                        {
                            new()
                            {
                                Description = $"Change the crossing operator to a state comparison ({newOp})",
                                Operations = new List<JsonPatchOperation>
                                {
                                    new() { Op = "replace", Path = $"{ev.Path}/operator", Value = JsonValue.Create(newOp) }
                                }
                            }
                        }
                    });
                }
            }
        }

        // Recurse
        for (int i = 0; i < group.Children.Count; i++)
        {
            var child = group.Children[i];
            var childPath = $"{path}/children/{i}";
            if (child is Condition c) AnalyzeCondition(c, childPath, defaultTf, findings);
            else if (child is ConditionGroup g) AnalyzeGroup(g, childPath, defaultTf, findings);
        }
    }

    private static void AnalyzeCondition(Condition condition, string path, Timeframe defaultTf, List<StaticFinding> findings)
    {
        if (!condition.Enabled) return;

        // RULE: TF001 - Timeframe Ambiguity
        // FIXED: Only check timeframe if the expression actually supports one
        bool leftSupportsTf = SupportsTimeframe(condition.Left);
        bool rightSupportsTf = SupportsTimeframe(condition.Right);

        var leftTf = GetExplicitTimeframe(condition.Left);
        var rightTf = GetExplicitTimeframe(condition.Right);

        if (leftSupportsTf && leftTf == null && rightSupportsTf && rightTf != null)
        {
            findings.Add(new StaticFinding
            {
                Id = $"TF001_{path}/left",
                Title = "Missing timeframe",
                Description = $"The left operand has no explicit timeframe, while the right operand uses {rightTf}. This will default to the strategy's base timeframe ({defaultTf}), which may not be intended.",
                Severity = FindingSeverity.Warning,
                Certainty = FindingCertainty.Confirmed,
                JsonPointer = $"{path}/left",
                SuggestedPatches = new List<CandidatePatch>
                {
                    new()
                    {
                        Description = $"Add explicit timeframe {rightTf} to match the right operand",
                        Operations = new List<JsonPatchOperation>
                        {
                            new() { Op = "add", Path = $"{path}/left/timeframe", Value = JsonValue.Create(rightTf.ToString()) }
                        }
                    }
                }
            });
        }
        else if (rightSupportsTf && rightTf == null && leftSupportsTf && leftTf != null)
        {
            findings.Add(new StaticFinding
            {
                Id = $"TF001_{path}/right",
                Title = "Missing timeframe",
                Description = $"The right operand has no explicit timeframe, while the left operand uses {leftTf}. This will default to the strategy's base timeframe ({defaultTf}), which may not be intended.",
                Severity = FindingSeverity.Warning,
                Certainty = FindingCertainty.Confirmed,
                JsonPointer = $"{path}/right",
                SuggestedPatches = new List<CandidatePatch>
                {
                    new()
                    {
                        Description = $"Add explicit timeframe {leftTf} to match the left operand",
                        Operations = new List<JsonPatchOperation>
                        {
                            new() { Op = "add", Path = $"{path}/right/timeframe", Value = JsonValue.Create(leftTf.ToString()) }
                        }
                    }
                }
            });
        }
    }

    // (Rest of file unchanged - only SupportsTimeframe is modified)

    private static bool SupportsTimeframe(ValueExpression expr)
    {
        // FIX: Recurse through calculations so TF001 checks aren't bypassed
        // when a calc expression contains timeframe-bearing operands.
        return expr switch
        {
            FieldExpression => true,
            IndicatorExpression => true,
            CalculationExpression c => SupportsTimeframe(c.Left) || SupportsTimeframe(c.Right),
            _ => false
        };
    }

    private static Timeframe? GetExplicitTimeframe(ValueExpression expr)
    {
        return expr switch
        {
            FieldExpression f => f.Timeframe,
            IndicatorExpression i => i.Timeframe,
            CalculationExpression c => GetExplicitTimeframe(c.Left) ?? GetExplicitTimeframe(c.Right),
            _ => null
        };
    }
}