using AlgoTrading.Models.Rules;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class DatabaseCrossoverLogger : IDisposable
{
    private readonly Dictionary<DateTimeOffset, DatabaseCandleRow> rows = new();
    private readonly List<BoundRuleExecution> conditionExecutions = new();
    private readonly HashSet<DateTimeOffset> loggedTimes = new();
    private readonly StreamWriter detailedLog;
    private readonly StreamWriter summaryLog;
    private readonly JsonSerializerOptions jsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly TimeZoneInfo timestampTimezone;
    internal int CrossoverCount { get; private set; }
    internal int StrategyMatchCount { get; private set; }
    internal IReadOnlySet<DateTimeOffset> LoggedTimes
    {
        get
        {
            return loggedTimes;
        }
    }

    internal DatabaseCrossoverLogger(string outputDirectory, IReadOnlyList<DatabaseCandleRow> sourceRows,
        RuleDefinition definition, RuleMarketData marketData, TimeZoneInfo timestampTimezone)
    {
        this.timestampTimezone = timestampTimezone;
        foreach (DatabaseCandleRow row in sourceRows) rows.Add(row.CompletedCandle.ClosedAt, row);
        var group = (ConditionGroup)definition.Condition;
        for (int conditionIndex = 0; conditionIndex < group.Conditions.Count; conditionIndex++)
        {
            var conditionRule = new RuleDefinition("Condition " + (conditionIndex + 1), definition.EvaluationTimeframe, group.Conditions[conditionIndex]);
            conditionExecutions.Add(RuleBinder.Bind(conditionRule).BindData(marketData));
        }
        detailedLog = new StreamWriter(Path.Combine(outputDirectory, "crossovers.jsonl"), false);
        summaryLog = new StreamWriter(Path.Combine(outputDirectory, "crossovers.csv"), false);
        summaryLog.WriteLine("CandleTimestamp,CompletedAt,PreviousExponentialAverage,PreviousNestedAverage,CurrentExponentialAverage,CurrentNestedAverage,Close,PreviousHigh,CrossedAbove,CloseAboveSuperTrend,CloseAboveExponentialAverage,CloseAbovePreviousHigh,AllConditionsMatched,NextCandleTimestamp");
    }

    internal void OnCrossoverDetected(object? sender, CrossoverEventArgs arguments)
    {
        CrossoverOccurrence occurrence = arguments.Occurrence;
        if (!loggedTimes.Add(occurrence.OccurredAt)) throw new InvalidOperationException("The database test received a duplicate crossover.");
        var conditionResults = new List<RuleEvaluation>();
        foreach (BoundRuleExecution execution in conditionExecutions) conditionResults.Add(execution.Evaluate(occurrence.OccurredAt));
        bool allConditionsMatched = true;
        foreach (RuleEvaluation result in conditionResults) allConditionsMatched &= result.IsMatch;
        if (allConditionsMatched != occurrence.OverallRuleEvaluation.IsMatch)
            throw new InvalidOperationException("The overall strategy result disagrees with its four individual conditions.");
        DatabaseCandleRow previousRow = rows[occurrence.Previous.Candle.ClosedAt];
        DatabaseCandleRow currentRow = rows[occurrence.Current.Candle.ClosedAt];
        DatabaseCandleRow? nextRow = occurrence.Next == null ? null : rows[occurrence.Next.Candle.ClosedAt];
        var entry = new DatabaseCrossoverLogEntry(occurrence, previousRow, currentRow, nextRow, conditionResults.AsReadOnly());
        detailedLog.WriteLine(JsonSerializer.Serialize(entry, jsonOptions));
        summaryLog.WriteLine(string.Join(",", currentRow.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            TimeZoneInfo.ConvertTime(occurrence.OccurredAt, timestampTimezone).ToString("O", CultureInfo.InvariantCulture),
            FormatNumber(occurrence.Previous.Left.Value), FormatNumber(occurrence.Previous.Right.Value),
            FormatNumber(occurrence.Current.Left.Value), FormatNumber(occurrence.Current.Right.Value),
            currentRow.Close.ToString(CultureInfo.InvariantCulture), previousRow.High.ToString(CultureInfo.InvariantCulture),
            conditionResults[0].Status, conditionResults[1].Status, conditionResults[2].Status, conditionResults[3].Status,
            occurrence.OverallRuleEvaluation.IsMatch, nextRow?.Timestamp.ToString("O", CultureInfo.InvariantCulture) ?? ""));
        CrossoverCount++;
        if (occurrence.OverallRuleEvaluation.IsMatch) StrategyMatchCount++;
    }

    private static string FormatNumber(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
    }

    public void Dispose()
    {
        detailedLog.Dispose();
        summaryLog.Dispose();
    }
}


