namespace AlgoTrading.Screener.Storage;

public sealed record ScreenerExecutionPlan(
    int FormatVersion,
    int GrammarVersion,
    string Name,
    string Identity,
    string Expression,
    string ExpressionFingerprint,
    string Timeframe,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<IndicatorRequirement> Indicators,
    HistoryRequirement History,
    EvaluationDefinition Evaluation,
    SqlRetrievalDefinition SqlRetrieval);

public sealed record IndicatorRequirement(
    string Identifier,
    string Indicator,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> Dependencies,
    int? BasePeriod,
    int OffsetCandles,
    int PreviousValueCandles,
    int? RequiredInputCandles,
    string StateDependency,
    string RetrievalStrategy);

public sealed record HistoryRequirement(
    string Timeframe,
    int? OverallRequiredCandles,
    string StateDependency,
    string RetrievalStrategy);

public sealed record EvaluationDefinition(
    string Mode,
    string EvaluationPoint,
    IReadOnlyList<string> SelectedColumns,
    IReadOnlyList<string> Sorting,
    int ResultLimit);

public sealed record SqlRetrievalDefinition(
    string Provider,
    string CommandText,
    IReadOnlyList<SqlParameterDefinition> Parameters);

public sealed record SqlParameterDefinition(string Name, string Type, bool Required);
