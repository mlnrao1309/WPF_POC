namespace AlgoTrading.Models;

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }
    public IReadOnlyList<ValidationError> Warnings { get; }
    public IReadOnlyList<string> SkippedChecks { get; }

    public ValidationResult(
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<ValidationError> warnings,
        IReadOnlyList<string> skippedChecks)
    {
        Errors = errors;
        Warnings = warnings;
        SkippedChecks = skippedChecks;
        IsValid = errors.Count == 0;
    }
}