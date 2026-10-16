using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.ScannerModel;

/// <summary>
/// Extension point for the Pine-derived critical-level helpers.
/// Concrete helper logic is intentionally deferred until those definitions
/// are supplied. A helper may declare extra candle requirements and populate
/// Instruments.Structures before RuleSets are evaluated.
/// </summary>
public interface ICriticalLevelHelper
{
    string Name { get; }

    void AddRequirements(ScannerRequirements requirements);

    void Prepare(
        Instruments instruments,
        DateTimeOffset evaluationTime);
}
