using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Services;

/// <summary>
/// Wraps the T1 validator. RULE_026 / RULE_027 / RULE_029 are always
/// skipped because the editor does not pass an IInstrumentLookup to T1.
/// The instrument-existence check is done separately by the caller.
/// </summary>
public sealed class RuleValidationService
{
    private readonly InstrumentLookupService _lookup;

    public RuleValidationService(InstrumentLookupService lookup)
    {
        _lookup = lookup;
    }

    public bool LookupAvailable => _lookup.IsAvailable;

    public ValidationResult Validate(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return RuleDefinitionValidator.Validate(rule);
    }

    /// <summary>
    /// Extra check not covered by T1 when no lookup is provided.
    /// Returns null when lookup is unavailable or the rule has no token.
    /// </summary>
    public string? CheckInstrumentExists(RuleDefinition rule)
    {
        if (!rule.InstrumentToken.HasValue) return null;
        if (!_lookup.IsAvailable) return null;

        return _lookup.TryGet(rule.InstrumentToken.Value, out _)
            ? null
            : "RULE_029: instrumentToken " + rule.InstrumentToken.Value +
              " does not exist in the Instruments table.";
    }
}