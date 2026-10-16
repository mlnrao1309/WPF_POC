using AlgoTrading.Models.Rules;

internal sealed record DatabaseCrossoverLogEntry(
    CrossoverOccurrence Crossover,
    DatabaseCandleRow PreviousDatabaseRow,
    DatabaseCandleRow CurrentDatabaseRow,
    DatabaseCandleRow? NextDatabaseRow,
    IReadOnlyList<RuleEvaluation> ConditionResults);
