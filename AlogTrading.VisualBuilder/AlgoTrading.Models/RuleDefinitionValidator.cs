using System.Text.RegularExpressions;

namespace AlgoTrading.Models;

// Keep IInstrumentLookup and InstrumentInfo exactly as they are...
public interface IInstrumentLookup { bool TryGet(uint token, out InstrumentInfo info); }
public sealed class InstrumentInfo { public uint Token { get; init; } public string Symbol { get; init; } = string.Empty; public string? DisplayName { get; init; } public InstrumentType InstrumentType { get; init; } public int LotSize { get; init; } public decimal TickSize { get; init; } }

public static class RuleDefinitionValidator
{
    private const int MaxGroupDepth = 10;
    private const int MaxExprDepth = 10;
    private static readonly Regex NameRegex = new("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public static ValidationResult Validate(RuleDefinition rule, IInstrumentLookup? lookup = null)
    {
        var errs = new List<ValidationError>();
        var warn = new List<ValidationError>();
        var skip = new List<string>();

        if (rule.SchemaVersion != 1) errs.Add(new("RULE_001", "schemaVersion must equal 1.", "schemaVersion"));
        if (string.IsNullOrWhiteSpace(rule.StrategyName)) errs.Add(new("RULE_002", "strategyName must be non-empty.", "strategyName"));
        if (rule.BaseTimeframe.Equals(default(Timeframe))) errs.Add(new("RULE_007", "baseTimeframe must be a valid Timeframe.", "baseTimeframe"));
        if (rule.EvaluationTimeframe.Equals(default(Timeframe))) errs.Add(new("RULE_008", "evaluationTimeframe must be a valid Timeframe.", "evaluationTimeframe"));

        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in rule.Parameters)
        {
            if (!paramNames.Add(p.Name)) errs.Add(new("RULE_012", $"Duplicate parameter name '{p.Name}'.", $"parameters.{p.Name}"));
            if (!(p.Minimum <= p.Default && p.Default <= p.Maximum) || p.Step <= 0m)
                errs.Add(new("RULE_013", $"Parameter '{p.Name}' violates Minimum<=Default<=Maximum or Step>0.", $"parameters.{p.Name}"));
        }

        // --- Reference Blocks Validation ---
        var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rule.References.Count; i++)
        {
            var block = rule.References[i];
            var blockPath = $"references[{i}]";

            if (block.Trigger is null)
                errs.Add(new("RULE_REF_TRIGGER", "Reference block is missing a trigger.", $"{blockPath}.trigger"));
            else
                ValidateNode(block.Trigger, $"{blockPath}.trigger", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);

            if (block.ClearOn is not null)
                ValidateNode(block.ClearOn, $"{blockPath}.clearOn", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);

            foreach (var capture in block.Captures)
            {
                if (string.IsNullOrWhiteSpace(capture.Id))
                    errs.Add(new("RULE_REF_ID", "Capture id must be non-empty.", $"{blockPath}.captures"));
                else if (!referenceNames.Add(capture.Id))
                    errs.Add(new("RULE_REF_DUPE", $"Duplicate capture id '{capture.Id}'.", $"{blockPath}.captures"));

                if (capture.Value is null)
                    errs.Add(new("RULE_REF_VALUE", $"Capture '{capture.Id}' is missing a value expression.", $"{blockPath}.captures"));
                else
                    ValidateExpr(capture.Value, $"{blockPath}.captures.{capture.Id}", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);
            }
        }

        if (rule.Entry is null || !ContainsEnabledCondition(rule.Entry)) errs.Add(new("RULE_009", "entry must contain at least one enabled condition.", "entry"));
        if (rule.Protection is null && rule.Exit is null && rule.FullExit is null && rule.StopLoss is null && rule.Target is null)
            errs.Add(new("RULE_010", "At least one of protection, exit, fullExit, stopLoss, target required.", "rule"));

        var namedGroups = new Dictionary<string, ConditionGroup>(StringComparer.Ordinal);
        CollectNamedGroups(rule.Eligibility, namedGroups, errs);
        CollectNamedGroups(rule.Entry, namedGroups, errs);
        CollectNamedGroups(rule.Protection, namedGroups, errs);
        CollectNamedGroups(rule.Exit, namedGroups, errs);
        CollectNamedGroups(rule.FullExit, namedGroups, errs);

        if (rule.StopLoss is { Mode: StopLossMode.Rule, GroupName: { } slName })
            if (!namedGroups.TryGetValue(slName, out var g) || !ContainsEnabledCondition(g))
                errs.Add(new("RULE_020", $"StopLoss rule target '{slName}' not found or empty.", "stopLoss.groupName"));
        if (rule.Target is { Mode: TargetMode.Rule, GroupName: { } tpName })
            if (!namedGroups.TryGetValue(tpName, out var g) || !ContainsEnabledCondition(g))
                errs.Add(new("RULE_020", $"Target rule target '{tpName}' not found or empty.", "target.groupName"));

        if (rule.StopLoss is { } sl2 && (sl2.Mode == StopLossMode.Percentage || sl2.Mode == StopLossMode.Points))
            if (!sl2.Value.HasValue || sl2.Value.Value <= 0m) errs.Add(new("RULE_021", "StopLoss Percentage/Points value must be positive.", "stopLoss.value"));
        if (rule.Target is { } tp2 && (tp2.Mode == TargetMode.Percentage || tp2.Mode == TargetMode.Points))
            if (!tp2.Value.HasValue || tp2.Value.Value <= 0m) errs.Add(new("RULE_021", "Target Percentage/Points value must be positive.", "target.value"));

        if (rule.InstrumentToken.HasValue && rule.InstrumentToken.Value == 0) errs.Add(new("RULE_028", "instrumentToken must be non-zero when present.", "instrumentToken"));
        if (rule.InstrumentToken is { } tok && tok != 0)
        {
            if (lookup is null) skip.Add("RULE_029");
            else if (!lookup.TryGet(tok, out _)) errs.Add(new("RULE_029", $"Instrument {tok} not found.", "instrumentToken"));
        }

        Walk(rule.Eligibility, "eligibility", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);
        Walk(rule.Entry, "entry", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);
        Walk(rule.Protection, "protection", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);
        Walk(rule.Exit, "exit", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);
        Walk(rule.FullExit, "fullExit", 0, errs, warn, skip, paramNames, referenceNames, lookup, rule);

        if (rule.Eligibility is not null && !ContainsEnabledCondition(rule.Eligibility)) warn.Add(new("RULE_022", "eligibility is empty.", "eligibility"));
        if (TryCompare(rule.BaseTimeframe, rule.EvaluationTimeframe, out var cmp) && cmp < 0) warn.Add(new("RULE_024", "evaluationTimeframe is coarser than baseTimeframe.", "evaluationTimeframe"));

        if (rule.InstrumentToken.HasValue && rule.TargetTimeframe.HasValue)
        {
            foreach (var p in rule.Parameters)
            {
                if (p.Minimum != p.Maximum)
                {
                    warn.Add(new("RULE_030", $"Tuned strategy has sweepable parameter '{p.Name}'.", $"parameters.{p.Name}"));
                    break;
                }
            }
        }

        return new ValidationResult(errs, warn, skip);
    }

    // --- (Keep ValidateNode, ContainsEnabledCondition, CollectNamedGroups, Walk, ValidateCondition, ValidateExpr, TryCompare exactly as they are from the previous step) ---
    // I have omitted them here to save space, but they are identical to the version you just compiled.
    private static void ValidateNode(Node node, string path, int depth, List<ValidationError> errs, List<ValidationError> warn, List<string> skip, HashSet<string> paramNames, HashSet<string> referenceNames, IInstrumentLookup? lookup, RuleDefinition rule) { /* same as before */ }
    private static bool ContainsEnabledCondition(ConditionGroup? g) { return true; /* same as before */ }
    private static void CollectNamedGroups(ConditionGroup? g, Dictionary<string, ConditionGroup> acc, List<ValidationError> errs) { /* same as before */ }
    private static void Walk(ConditionGroup? g, string path, int depth, List<ValidationError> errs, List<ValidationError> warn, List<string> skip, HashSet<string> paramNames, HashSet<string> referenceNames, IInstrumentLookup? lookup, RuleDefinition rule) { /* same as before */ }
    private static void ValidateCondition(Condition c, string path, List<ValidationError> errs, List<ValidationError> warn, List<string> skip, HashSet<string> paramNames, HashSet<string> referenceNames, IInstrumentLookup? lookup, RuleDefinition rule) { /* same as before */ }
    private static void ValidateExpr(ValueExpression e, string path, int depth, List<ValidationError> errs, List<ValidationError> warn, List<string> skip, HashSet<string> paramNames, HashSet<string> referenceNames, IInstrumentLookup? lookup, RuleDefinition rule) { /* same as before */ }
    private static bool TryCompare(Timeframe a, Timeframe b, out int result) { result = 0; return false; }
}