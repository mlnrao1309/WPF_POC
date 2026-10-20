using System.Text.RegularExpressions;

namespace AlgoTrading.Models;

// Minimal lookup used by RULE_026/027/029. Full implementation lives in AlgoTrading.Data.
public interface IInstrumentLookup
{
    bool TryGet(uint token, out InstrumentInfo info);
}

public sealed class InstrumentInfo
{
    public uint Token { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public InstrumentType InstrumentType { get; init; }
    public int LotSize { get; init; }
    public decimal TickSize { get; init; }
}

public static class RuleDefinitionValidator
{
    private const int MaxGroupDepth = 10;
    private const int MaxExprDepth  = 10;
    private static readonly Regex NameRegex = new("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public static ValidationResult Validate(RuleDefinition rule, IInstrumentLookup? lookup = null)
    {
        var errs = new List<ValidationError>();
        var warn = new List<ValidationError>();
        var skip = new List<string>();

        // RULE_001
        if (rule.SchemaVersion != 1)
            errs.Add(new("RULE_001", "schemaVersion must equal 1.", "schemaVersion"));

        // RULE_002
        if (string.IsNullOrWhiteSpace(rule.StrategyName))
            errs.Add(new("RULE_002", "strategyName must be non-empty.", "strategyName"));

        // RULE_007 (baseTimeframe)
        if (rule.BaseTimeframe.Equals(default(Timeframe)))
            errs.Add(new("RULE_007", "baseTimeframe must be a valid Timeframe.", "baseTimeframe"));

        // RULE_008 (evaluationTimeframe)
        if (rule.EvaluationTimeframe.Equals(default(Timeframe)))
            errs.Add(new("RULE_008", "evaluationTimeframe must be a valid Timeframe.", "evaluationTimeframe"));

        // Parameters
        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in rule.Parameters)
        {
            // RULE_012
            if (!paramNames.Add(p.Name))
                errs.Add(new("RULE_012", $"Duplicate parameter name '{p.Name}'.", $"parameters.{p.Name}"));
            // RULE_013
            if (!(p.Minimum <= p.Default && p.Default <= p.Maximum) || p.Step <= 0m)
                errs.Add(new("RULE_013", $"Parameter '{p.Name}' violates Minimum<=Default<=Maximum or Step>0.", $"parameters.{p.Name}"));
        }

        // RULE_009 â€” entry non-empty recursively
        if (rule.Entry is null || !ContainsEnabledCondition(rule.Entry))
            errs.Add(new("RULE_009", "entry must contain at least one enabled condition.", "entry"));

        // RULE_010 â€” at least one exit path
        if (rule.Protection is null && rule.Exit is null && rule.FullExit is null
            && rule.StopLoss is null && rule.Target is null)
            errs.Add(new("RULE_010", "At least one of protection, exit, fullExit, stopLoss, target required.", "rule"));

        // Named groups
        var namedGroups = new Dictionary<string, ConditionGroup>(StringComparer.Ordinal);
        CollectNamedGroups(rule.Eligibility, namedGroups, errs);
        CollectNamedGroups(rule.Entry,        namedGroups, errs);
        CollectNamedGroups(rule.Protection,   namedGroups, errs);
        CollectNamedGroups(rule.Exit,         namedGroups, errs);
        CollectNamedGroups(rule.FullExit,     namedGroups, errs);

        // SL/TP rule target checks
        if (rule.StopLoss is { Mode: StopLossMode.Rule, GroupName: { } slName })
        {
            if (!namedGroups.TryGetValue(slName, out var g) || !ContainsEnabledCondition(g))
                errs.Add(new("RULE_020", $"StopLoss rule target '{slName}' not found or empty.", "stopLoss.groupName"));
        }
        if (rule.Target is { Mode: TargetMode.Rule, GroupName: { } tpName })
        {
            if (!namedGroups.TryGetValue(tpName, out var g) || !ContainsEnabledCondition(g))
                errs.Add(new("RULE_020", $"Target rule target '{tpName}' not found or empty.", "target.groupName"));
        }

        // RULE_021 â€” Percentage/Points positive value
        if (rule.StopLoss is { } sl2 && (sl2.Mode == StopLossMode.Percentage || sl2.Mode == StopLossMode.Points))
        {
            if (!sl2.Value.HasValue || sl2.Value.Value <= 0m)
                errs.Add(new("RULE_021", "StopLoss Percentage/Points value must be positive.", "stopLoss.value"));
        }
        if (rule.Target is { } tp2 && (tp2.Mode == TargetMode.Percentage || tp2.Mode == TargetMode.Points))
        {
            if (!tp2.Value.HasValue || tp2.Value.Value <= 0m)
                errs.Add(new("RULE_021", "Target Percentage/Points value must be positive.", "target.value"));
        }

        // RULE_028 â€” instrumentToken non-zero when present
        if (rule.InstrumentToken.HasValue && rule.InstrumentToken.Value == 0)
            errs.Add(new("RULE_028", "instrumentToken must be non-zero when present.", "instrumentToken"));

        // RULE_029 â€” instrument exists (skipped without lookup)
        if (rule.InstrumentToken is { } tok && tok != 0)
        {
            if (lookup is null)
            {
                skip.Add("RULE_029");
            }
            else if (!lookup.TryGet(tok, out _))
            {
                errs.Add(new("RULE_029", $"Instrument {tok} not found.", "instrumentToken"));
            }
        }

        // Walk every group for structural / RULE_014..RULE_032 checks
        Walk(rule.Eligibility, "eligibility", 0, errs, warn, skip, paramNames, lookup, rule);
        Walk(rule.Entry,        "entry",       0, errs, warn, skip, paramNames, lookup, rule);
        Walk(rule.Protection,   "protection",  0, errs, warn, skip, paramNames, lookup, rule);
        Walk(rule.Exit,         "exit",        0, errs, warn, skip, paramNames, lookup, rule);
        Walk(rule.FullExit,     "fullExit",    0, errs, warn, skip, paramNames, lookup, rule);

        // RULE_022 â€” empty eligibility
        if (rule.Eligibility is not null && !ContainsEnabledCondition(rule.Eligibility))
            warn.Add(new("RULE_022", "eligibility is empty.", "eligibility"));

        // RULE_024 â€” evaluation coarser than base
        if (TryCompare(rule.BaseTimeframe, rule.EvaluationTimeframe, out var cmp) && cmp < 0)
            warn.Add(new("RULE_024", "evaluationTimeframe is coarser than baseTimeframe.", "evaluationTimeframe"));

        // RULE_030 â€” tuned strategy with sweepable params
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

    // ---------- helpers ----------

    private static bool ContainsEnabledCondition(ConditionGroup? g)
    {
        if (g is null || !g.Enabled) return false;
        foreach (var c in g.Children)
        {
            if (c is Condition cond && cond.Enabled) return true;
            if (c is ConditionGroup grp && ContainsEnabledCondition(grp)) return true;
        }
        return false;
    }

    private static void CollectNamedGroups(ConditionGroup? g, Dictionary<string, ConditionGroup> acc, List<ValidationError> errs)
    {
        if (g is null) return;
        if (g.Name is { Length: > 0 } n)
        {
            if (!NameRegex.IsMatch(n))
                errs.Add(new("RULE_019", $"Invalid group name '{n}'.", "group.name"));
            else if (!acc.TryAdd(n, g))
                errs.Add(new("RULE_019", $"Duplicate group name '{n}'.", "group.name"));
        }
        foreach (var c in g.Children)
            if (c is ConditionGroup grp) CollectNamedGroups(grp, acc, errs);
    }

    private static void Walk(
        ConditionGroup? g, string path, int depth,
        List<ValidationError> errs, List<ValidationError> warn, List<string> skip,
        HashSet<string> paramNames, IInstrumentLookup? lookup, RuleDefinition rule)
    {
        if (g is null) return;

        // RULE_017
        if (depth > MaxGroupDepth)
        {
            errs.Add(new("RULE_017", "Group depth exceeds 10.", path));
            return;
        }

        if (g.Name is { Length: > 0 } n && !NameRegex.IsMatch(n))
            errs.Add(new("RULE_019", $"Invalid group name '{n}'.", path + ".name"));

        bool sawDisabled = false;
        foreach (var c in g.Children)
        {
            if (c is Condition cond)
            {
                if (!cond.Enabled) { sawDisabled = true; continue; }
                ValidateCondition(cond, path + ".condition", errs, warn, skip, paramNames, lookup, rule);
            }
            else if (c is ConditionGroup grp)
            {
                Walk(grp, path + ".group", depth + 1, errs, warn, skip, paramNames, lookup, rule);
            }
        }

        // RULE_023
        if (sawDisabled && g.Enabled)
            warn.Add(new("RULE_023", "Disabled condition inside an enabled group.", path));
    }

    private static void ValidateCondition(
        Condition c, string path,
        List<ValidationError> errs, List<ValidationError> warn, List<string> skip,
        HashSet<string> paramNames, IInstrumentLookup? lookup, RuleDefinition rule)
    {
        ValidateExpr(c.Left, path + ".left", 0, errs, warn, skip, paramNames, lookup, rule);
        ValidateExpr(c.Right, path + ".right", 0, errs, warn, skip, paramNames, lookup, rule);
        // RULE_031 â€” Opened/Closed operators constrain left field.
        if (c.Operator is Operator.OpenedAbove or Operator.OpenedBelow or
                            Operator.ClosedAbove or Operator.ClosedBelow)
        {
            if (c.Left is not FieldExpression f)
                errs.Add(new("RULE_031", $"{c.Operator} left side must be a field expression.", path + ".left"));
            else if (c.Operator is Operator.OpenedAbove or Operator.OpenedBelow && f.Field != Field.Open)
                errs.Add(new("RULE_031", $"{c.Operator} left field must be Open.", path + ".left.field"));
            else if (c.Operator is Operator.ClosedAbove or Operator.ClosedBelow && f.Field != Field.Close)
                errs.Add(new("RULE_031", $"{c.Operator} left field must be Close.", path + ".left.field"));
        }
    }

    private static void ValidateExpr(
        ValueExpression e, string path, int depth,
        List<ValidationError> errs, List<ValidationError> warn, List<string> skip,
        HashSet<string> paramNames, IInstrumentLookup? lookup, RuleDefinition rule)
    {        // RULE_018
        if (depth > MaxExprDepth)
        {
            errs.Add(new("RULE_018", "Expression recursion depth exceeds 10.", path));
            return;
        }

        switch (e)
        {
            case FieldExpression f:
                // RULE_016
                if (f.Offset < 0)
                    errs.Add(new("RULE_016", "offset must be >= 0.", path + ".offset"));
                // RULE_026 / RULE_027
                if (f.Field == Field.OI && lookup is not null && rule.InstrumentToken is { } tok1
                    && lookup.TryGet(tok1, out var info1) && info1.InstrumentType != InstrumentType.Future && info1.InstrumentType != InstrumentType.Option)
                {
                    warn.Add(new("RULE_026", "OI used on equity/index â€” value always 0.", path));
                }
                if (f.Field == Field.Volume && lookup is not null && rule.InstrumentToken is { } tok2
                    && lookup.TryGet(tok2, out var info2) && info2.InstrumentType == InstrumentType.Index)
                {
                    warn.Add(new("RULE_027", "Volume used on index â€” value always 0.", path));
                }
                break;

            case NumberExpression:
                break;

            case ParameterExpression p:
                // RULE_011
                if (!paramNames.Contains(p.Name))
                    errs.Add(new("RULE_011", $"Parameter '{p.Name}' not declared.", path + ".name"));
                break;

            case IndicatorExpression ind:
                // RULE_014 â€” kind must be registered. Registry is in AlgoTrading.Indicators (T5); T1 checks the enum has a value.
                if (!Enum.IsDefined(typeof(IndicatorKind), ind.Indicator))
                    errs.Add(new("RULE_014", $"Indicator '{ind.Indicator}' not registered.", path + ".indicator"));
                // RULE_016
                if (ind.Offset < 0)
                    errs.Add(new("RULE_016", "offset must be >= 0.", path + ".offset"));
                // RULE_032 â€” declared outputs are checked in T5 via descriptor. T1 makes no assertion.
                if (ind.Source is not null)
                    ValidateExpr(ind.Source, path + ".source", depth + 1, errs, warn, skip, paramNames, lookup, rule);
                break;
            case CalculationExpression calc:
                ValidateExpr(calc.Left,  path + ".left",  depth + 1, errs, warn, skip, paramNames, lookup, rule);
                ValidateExpr(calc.Right, path + ".right", depth + 1, errs, warn, skip, paramNames, lookup, rule);
                break;
        }
    }

    private static bool TryCompare(Timeframe a, Timeframe b, out int result)
    {
        try { result = a.CompareTo(b); return true; }
        catch { result = 0; return false; }
    }
}