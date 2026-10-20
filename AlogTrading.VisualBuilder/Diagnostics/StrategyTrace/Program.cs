using System.Globalization;
using System.Text;
using System.Text.Json;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Data;
using AlgoTrading.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StrategyTrace;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: StrategyTrace <rule.json> <appsettings.json> <trace.csv> <selection.txt>");
            return 1;
        }

        try
        {
            Run(args[0], args[1], args[2], args[3]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Run(string rulePath, string settingsPath, string tracePath, string selectionPath)
    {
        var ruleJson = File.ReadAllText(rulePath);
        var rule = RuleDefinitionJson.Deserialize(ruleJson);
        var connectionString = ReadConnectionString(settingsPath);
        var selection = SelectData(connectionString, rule.EvaluationTimeframe.ToString());

        var fromUtc = DateTime.SpecifyKind(selection.From.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(selection.To.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        var series = SqlBarSeries.Load(
            connectionString, selection.InstrumentToken,
            rule.EvaluationTimeframe, fromUtc, toUtc);

        if (series.Count == 0)
            throw new InvalidOperationException("No evaluation-timeframe candles were loaded.");

        var cache = new TimeframeCache(series, series.DailySeries);
        var accessor = new TimeframeAccessor(series.InstrumentToken, series, cache);
        var indicators = new IndicatorProvider(accessor);
        var evaluator = new ExpressionEvaluator(accessor, indicators, BuildParameters(rule));
        var conditions = new List<TracedCondition>();

        AddGroupConditions(conditions, "Eligibility", rule.Eligibility);
        AddGroupConditions(conditions, "Entry", rule.Entry);
        AddGroupConditions(conditions, "Protection", rule.Protection);
        AddGroupConditions(conditions, "Exit", rule.Exit);
        AddGroupConditions(conditions, "FullExit", rule.FullExit);

        WriteTrace(tracePath, rule, series, evaluator, conditions);
        WriteSelection(selectionPath, selection, series.Count, tracePath);
    }

    private static string ReadConnectionString(string settingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement
            .GetProperty("Data")
            .GetProperty("Sql")
            .GetProperty("ConnectionString")
            .GetString() ?? throw new InvalidOperationException("SQL connection string is missing.");
    }

    private static DataSelection SelectData(string connectionString, string timeframe)
    {
        const string sql = """
SELECT TOP (1)
       InstrumentToken,
       MIN(DateTime) AS MinDate,
       MAX(DateTime) AS MaxDate,
       COUNT_BIG(*) AS BarCount
FROM Candles
WHERE UPPER(Timeframe) = UPPER(@timeframe)
GROUP BY InstrumentToken
HAVING COUNT_BIG(*) >= 60
ORDER BY COUNT_BIG(*) DESC, InstrumentToken ASC;
""";

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        var row = connection.QuerySingleOrDefault<DataSelectionRow>(sql, new { timeframe })
            ?? throw new InvalidOperationException("No instrument has at least 60 candles for timeframe " + timeframe + ".");

        var from = row.MaxDate.AddDays(-180);
        if (from < row.MinDate)
            from = row.MinDate;

        return new DataSelection
        {
            InstrumentToken = checked((uint)row.InstrumentToken),
            From = from,
            To = row.MaxDate,
            AvailableBars = row.BarCount
        };
    }

    private static Dictionary<string, decimal> BuildParameters(RuleDefinition rule)
    {
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var parameter in rule.Parameters)
            parameters[parameter.Name] = parameter.Default;
        return parameters;
    }

    private static void AddGroupConditions(
        List<TracedCondition> output, string section, ConditionGroup? group)
    {
        if (group is null)
            return;

        AddNodes(output, section, group.Enabled, group.Children);
    }

    private static void AddNodes(
        List<TracedCondition> output, string section,
        bool sectionEnabled, IReadOnlyList<Node> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is Condition condition)
            {
                output.Add(new TracedCondition
                {
                    Section = section,
                    SectionEnabled = sectionEnabled,
                    Index = output.Count + 1,
                    Condition = condition
                });
            }
            else if (nodes[i] is ConditionGroup childGroup)
            {
                AddNodes(
                    output, section + "/Group" + (i + 1).ToString(CultureInfo.InvariantCulture),
                    sectionEnabled && childGroup.Enabled, childGroup.Children);
            }
        }
    }

    private static void WriteTrace(
        string path, RuleDefinition rule, IBarSeries series,
        ExpressionEvaluator evaluator, IReadOnlyList<TracedCondition> conditions)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(
            "DateTimeUtc,Section,SectionEnabled,ConditionEnabled,ConditionIndex,Operator," +
            "LeftExpression,RightExpression,LeftPrevious,RightPrevious,LeftCurrent,RightCurrent,Result");

        for (int barIndex = 0; barIndex < series.Count; barIndex++)
        {
            for (int conditionIndex = 0; conditionIndex < conditions.Count; conditionIndex++)
            {
                WriteCondition(
                    writer, rule.EvaluationTimeframe, series.DateTime.Span[barIndex],
                    barIndex, evaluator, conditions[conditionIndex]);
            }
        }
    }

    private static void WriteCondition(
        StreamWriter writer, Timeframe defaultTimeframe, DateTime dateTime,
        int barIndex, ExpressionEvaluator evaluator, TracedCondition traced)
    {
        var condition = traced.Condition;
        bool hasLeft = evaluator.TryEvaluate(
            condition.Left, defaultTimeframe, barIndex, 0, out var leftCurrent);
        bool hasRight = evaluator.TryEvaluate(
            condition.Right, defaultTimeframe, barIndex, 0, out var rightCurrent);

        bool crossover = condition.Operator is Operator.CrossedAbove or Operator.CrossedBelow;
        bool hasLeftPrevious = false;
        bool hasRightPrevious = false;
        decimal leftPrevious = 0m;
        decimal rightPrevious = 0m;
        if (crossover)
        {
            hasLeftPrevious = evaluator.TryEvaluate(
                condition.Left, defaultTimeframe, barIndex, 1, out leftPrevious);
            hasRightPrevious = evaluator.TryEvaluate(
                condition.Right, defaultTimeframe, barIndex, 1, out rightPrevious);
        }

        bool result = Evaluate(
            condition.Operator,
            hasLeft, leftCurrent,
            hasRight, rightCurrent,
            hasLeftPrevious, leftPrevious,
            hasRightPrevious, rightPrevious);

        var values = new string[]
        {
            dateTime.ToString("O", CultureInfo.InvariantCulture),
            traced.Section,
            traced.SectionEnabled.ToString(CultureInfo.InvariantCulture),
            condition.Enabled.ToString(CultureInfo.InvariantCulture),
            traced.Index.ToString(CultureInfo.InvariantCulture),
            condition.Operator.ToString(),
            Describe(condition.Left, defaultTimeframe),
            Describe(condition.Right, defaultTimeframe),
            FormatValue(hasLeftPrevious, leftPrevious),
            FormatValue(hasRightPrevious, rightPrevious),
            FormatValue(hasLeft, leftCurrent),
            FormatValue(hasRight, rightCurrent),
            result.ToString(CultureInfo.InvariantCulture)
        };
        writer.WriteLine(string.Join(',', values.Select(Escape)));
    }

    private static bool Evaluate(
        Operator op,
        bool hasLeft, decimal left,
        bool hasRight, decimal right,
        bool hasLeftPrevious, decimal leftPrevious,
        bool hasRightPrevious, decimal rightPrevious)
    {
        if (!hasLeft || !hasRight)
            return false;

        return op switch
        {
            Operator.GreaterThan => left > right,
            Operator.LessThan => left < right,
            Operator.GreaterOrEqual => left >= right,
            Operator.LessOrEqual => left <= right,
            Operator.Equal => left == right,
            Operator.NotEqual => left != right,
            Operator.OpenedAbove => left > right,
            Operator.OpenedBelow => left < right,
            Operator.ClosedAbove => left > right,
            Operator.ClosedBelow => left < right,
            Operator.CrossedAbove => hasLeftPrevious && hasRightPrevious &&
                                     left > right && leftPrevious <= rightPrevious,
            Operator.CrossedBelow => hasLeftPrevious && hasRightPrevious &&
                                     left < right && leftPrevious >= rightPrevious,
            _ => false
        };
    }

    private static string Describe(ValueExpression expression, Timeframe defaultTimeframe)
    {
        if (expression is FieldExpression field)
        {
            var timeframe = field.Timeframe ?? defaultTimeframe;
            return timeframe + ":" + field.Field + "[" + field.Offset + "]";
        }

        if (expression is IndicatorExpression indicator)
        {
            var timeframe = indicator.Timeframe ?? defaultTimeframe;
            var output = string.IsNullOrWhiteSpace(indicator.Output) ? "default" : indicator.Output;
            return timeframe + ":" + indicator.Indicator + "." + output +
                   "[" + indicator.Offset + "]" + DescribeParameters(indicator.Params);
        }

        if (expression is NumberExpression number)
            return number.Value.ToString(CultureInfo.InvariantCulture);
        if (expression is ParameterExpression parameter)
            return "$" + parameter.Name;
        if (expression is CalculationExpression calculation)
            return calculation.Op.ToString();
        return expression.GetType().Name;
    }

    private static string DescribeParameters(IReadOnlyDictionary<string, decimal>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return "";

        var pairs = parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
        return "(" + string.Join(';', pairs) + ")";
    }

    private static string FormatValue(bool hasValue, decimal value)
    {
        return hasValue ? value.ToString(CultureInfo.InvariantCulture) : "";
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void WriteSelection(
        string path, DataSelection selection, int loadedBars, string tracePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = new string[]
        {
            "InstrumentToken=" + selection.InstrumentToken.ToString(CultureInfo.InvariantCulture),
            "From=" + selection.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "To=" + selection.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "AvailableBars=" + selection.AvailableBars.ToString(CultureInfo.InvariantCulture),
            "LoadedBars=" + loadedBars.ToString(CultureInfo.InvariantCulture),
            "TracePath=" + Path.GetFullPath(tracePath)
        };
        File.WriteAllLines(path, lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private sealed class TracedCondition
    {
        public string Section { get; init; } = "";
        public bool SectionEnabled { get; init; }
        public int Index { get; init; }
        public Condition Condition { get; init; } = null!;
    }

    private sealed class DataSelectionRow
    {
        public int InstrumentToken { get; init; }
        public DateTime MinDate { get; init; }
        public DateTime MaxDate { get; init; }
        public long BarCount { get; init; }
    }

    private sealed class DataSelection
    {
        public uint InstrumentToken { get; init; }
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public long AvailableBars { get; init; }
    }
}
