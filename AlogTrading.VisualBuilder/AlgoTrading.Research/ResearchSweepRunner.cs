using AlgoTrading.Backtest;
using AlgoTrading.Models;
using AlgoTrading.Models.Analysis;
using AlgoTrading.Research.Analysis;

namespace AlgoTrading.Research;

public static class ResearchSweepRunner
{
    /// <summary>
    /// Runs a parameter sweep while enforcing M1 static analysis gates.
    /// </summary>
    public static SweepResult RunWithStaticGates(
        RuleDefinition rule,
        AlgoTrading.Data.IBarSeries series,
        AlgoTrading.Backtest.BacktestEngineOptions options,
        SweepCriterion criterion,
        IReadOnlyList<SweepBinding> bindings,
        int maxCombinations,
        CancellationToken ct = default)
    {
        // Define the M1 validity check
        Func<RuleDefinition, IEnumerable<string>> validityCheck = (candidate) =>
        {
            var findings = RuleStaticAnalyzer.Analyze(candidate);
            
            // Block only findings that are marked as Blocker or HighReview
            return findings
                .Where(f => f.Severity == FindingSeverity.Blocker || f.Severity == FindingSeverity.HighReview)
                .Select(f => f.Description);
        };

        return ParameterSweep.Run(
            rule, series, options, criterion, bindings, validityCheck, maxCombinations, ct);
    }
}