using System.Security.Cryptography;
using System.Text;
using AlgoTrading.Screener.Dsl;
using DslIndex = AlgoTrading.Screener.Dsl.Index;

namespace AlgoTrading.Screener.Storage;

public sealed class ScreenerExecutionPlanBuilder
{
    public const int CurrentFormatVersion = 1;

    public ScreenerExecutionPlan Build(string name, string expression, string timeframe)
    {
        string safeName = ScreenerPlanStore.ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);

        QueryNode query = Parser.Parse(expression);
        var analysis = new DependencyAnalysis(query);
        analysis.Analyze();
        bool finite = QueryHistoryRequirements.TryGetFiniteBars(query, out int bars);
        string strategy = finite ? "LatestNPerInstrument" : "FullAvailableHistory";
        string state = finite ? "FiniteWindow" : "RequiresCompleteAvailableState";
        string normalized = expression.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{DslGrammar.CurrentVersion}\n{timeframe.Trim().ToUpperInvariant()}\n{normalized}")));

        return new ScreenerExecutionPlan(
            CurrentFormatVersion,
            DslGrammar.CurrentVersion,
            safeName,
            fingerprint[..16],
            expression,
            fingerprint,
            timeframe.Trim().ToUpperInvariant(),
            analysis.Fields.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            analysis.Indicators.ToArray(),
            new HistoryRequirement(timeframe.Trim().ToUpperInvariant(), finite ? bars : null, state, strategy),
            new EvaluationDefinition(query.PerBar ? "PerBar" : "PerInstrument", query.PerBar ? "EachBar" : "LatestBar",
                query.Select.Select(x => analysis.Format(x.Value)).ToArray(),
                query.OrderBy.Select(x => analysis.Format(x.Value) + (x.Descending ? " DESC" : " ASC")).ToArray(),
                query.Take is > 0 ? query.Take.Value : 1000),
            CreateSql(analysis.Fields, finite));
    }

    private static SqlRetrievalDefinition CreateSql(IEnumerable<string> fields, bool finite)
    {
        var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"] = "vw.[Open]", ["High"] = "vw.High", ["Low"] = "vw.Low",
            ["Close"] = "vw.[Close]", ["Volume"] = "vw.Volume", ["OI"] = "vw.OI",
            ["Time"] = "vw.TimeStamp", ["Hour"] = "vw.TimeStamp", ["Minute"] = "vw.TimeStamp",
            ["SessionId"] = "vw.TimeStamp", ["InstrumentToken"] = "vw.InstrumentToken",
            ["Symbol"] = "vw.TradingSymbol", ["Segment"] = "vw.Segment", ["Exchange"] = "vw.Exchange"
        };
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["InstrumentToken"] = "vw.InstrumentToken",
            ["TimeStamp"] = "vw.TimeStamp",
            ["TradingSymbol"] = "vw.TradingSymbol"
        };
        foreach (string field in fields)
            if (allowed.TryGetValue(field, out string? column))
                columns.TryAdd(column[(column.LastIndexOf('.') + 1)..].Trim('[', ']'), column);
        string innerColumns = string.Join(", ", columns.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Value + " AS [" + x.Key + "]"));
        string outerColumns = string.Join(", ", columns.Keys.Order(StringComparer.Ordinal)
            .Select(x => "ranked.[" + x + "]"));
        string source = $"SELECT {innerColumns}, ROW_NUMBER() OVER (PARTITION BY vw.InstrumentToken ORDER BY vw.TimeStamp DESC) AS rn FROM dbo.vw_InstrumentBars AS vw INNER JOIN OPENJSON(@InstrumentTokensJson) WITH (InstrumentToken bigint '$') AS requested ON requested.InstrumentToken = vw.InstrumentToken WHERE vw.TimeFrame = @TimeFrame";
        string sql = finite
            ? $"SELECT {outerColumns} FROM ({source}) AS ranked WHERE ranked.rn <= @RequiredBars ORDER BY ranked.InstrumentToken, ranked.TimeStamp;"
            : $"SELECT {outerColumns} FROM ({source}) AS ranked ORDER BY ranked.InstrumentToken, ranked.TimeStamp;";
        var parameters = new List<SqlParameterDefinition>
        {
            new("@InstrumentTokensJson", "nvarchar(max)", true),
            new("@TimeFrame", "varchar(10)", true)
        };
        if (finite) parameters.Add(new("@RequiredBars", "int", true));
        return new SqlRetrievalDefinition("Microsoft SQL Server", sql, parameters);
    }

    private sealed class DependencyAnalysis
    {
        private readonly QueryNode query;
        private readonly Dictionary<string, Expr> lets;
        private readonly HashSet<string> indicatorIds = new(StringComparer.Ordinal);
        public HashSet<string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IndicatorRequirement> Indicators { get; } = new();

        public DependencyAnalysis(QueryNode query)
        {
            this.query = query;
            lets = query.Lets.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }

        public void Analyze()
        {
            foreach (LetClause item in query.Lets) Visit(item.Value, 0, 0);
            if (query.Where is not null) Visit(query.Where, 0, 0);
            foreach (SelectItem item in query.Select) Visit(item.Value, 0, 0);
            foreach (OrderItem item in query.OrderBy) Visit(item.Value, 0, 0);
        }

        private void Visit(Expr expression, int offset, int previous)
        {
            switch (expression)
            {
                case Member member:
                    Fields.Add(member.Name);
                    Visit(member.Target, offset, previous);
                    break;
                case Ident identifier when lets.TryGetValue(identifier.Name, out Expr? value):
                    Visit(value, offset, previous);
                    break;
                case DslIndex index:
                    int indexedOffset = index.Offset is Num n && n.Value >= 0 && n.Value == decimal.Truncate(n.Value)
                        ? decimal.ToInt32(n.Value) : 0;
                    Visit(index.Target, checked(offset + indexedOffset), previous);
                    break;
                case Call call:
                    foreach (Expr arg in call.Args) Visit(arg, offset, previous);
                    AddIndicator(call, offset, previous);
                    break;
                case Binary binary:
                    int prior = binary.Op is "crossedabove" or "crossedbelow" or "touchedabove" or "touchedbelow" ? 1 : previous;
                    Visit(binary.Left, offset, prior);
                    Visit(binary.Right, offset, prior);
                    break;
                case Unary unary: Visit(unary.Operand, offset, previous); break;
                case Between between:
                    Visit(between.Value, offset, previous); Visit(between.Lower, offset, previous); Visit(between.Upper, offset, previous);
                    break;
            }
        }

        private void AddIndicator(Call call, int offset, int previous)
        {
            string id = Format(call);
            if (!indicatorIds.Add(id)) return;
            QueryNode single = new(query.SourceVar, query.SourceCollection, query.Lets, null, Array.Empty<OrderItem>(),
                new[] { new SelectItem(call, null) }, null, false);
            bool finite = QueryHistoryRequirements.TryGetFiniteBars(single, out int bars);
            Expr[] inputs = call.Args.Where(x => CollectFields(x).Any()).ToArray();
            Expr[] parameters = call.Args.Where(x => !CollectFields(x).Any()).ToArray();
            int? period = parameters.OfType<Num>()
                .Where(x => x.Value > 0 && x.Value == decimal.Truncate(x.Value) && x.Value <= int.MaxValue)
                .Select(x => (int?)x.Value).FirstOrDefault();
            Indicators.Add(new IndicatorRequirement(id, call.Name.ToUpperInvariant(),
                inputs.SelectMany(CollectFields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                parameters.Select(Format).ToArray(),
                call.Args.SelectMany(CollectCalls).Select(Format).Distinct(StringComparer.Ordinal).ToArray(), period, offset, previous,
                finite ? checked(bars + offset + previous) : null,
                finite ? "FiniteWindow" : "RequiresCompleteAvailableState",
                finite ? "LatestNPerInstrument" : "FullAvailableHistory"));
        }

        private IEnumerable<string> CollectFields(Expr expression)
        {
            if (expression is Member member) yield return member.Name;
            if (expression is Ident identifier && lets.TryGetValue(identifier.Name, out Expr? value))
            {
                foreach (string field in CollectFields(value)) yield return field;
                yield break;
            }
            foreach (Expr child in Children(expression))
                foreach (string field in CollectFields(child)) yield return field;
        }

        private IEnumerable<Call> CollectCalls(Expr expression)
        {
            if (expression is Call call) yield return call;
            if (expression is Ident identifier && lets.TryGetValue(identifier.Name, out Expr? value))
            {
                foreach (Call nested in CollectCalls(value)) yield return nested;
                yield break;
            }
            foreach (Expr child in Children(expression))
                foreach (Call nested in CollectCalls(child)) yield return nested;
        }

        private static IEnumerable<Expr> Children(Expr expression) => expression switch
        {
            DslIndex x => new[] { x.Target, x.Offset }, Call x => x.Args, Unary x => new[] { x.Operand },
            Binary x => new[] { x.Left, x.Right }, Between x => new[] { x.Value, x.Lower, x.Upper },
            Member x => new[] { x.Target }, _ => Array.Empty<Expr>()
        };

        public string Format(Expr expression) => expression switch
        {
            Num x => x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), Str x => "\"" + x.Value + "\"",
            BoolLit x => x.Value ? "true" : "false", Ident x => x.Name, Member x => Format(x.Target) + "." + x.Name,
            DslIndex x => Format(x.Target) + "[" + Format(x.Offset) + "]", Call x => x.Name + "(" + string.Join(", ", x.Args.Select(Format)) + ")",
            Unary x => x.Op + " " + Format(x.Operand), Binary x => Format(x.Left) + " " + x.Op + " " + Format(x.Right),
            Between x => Format(x.Value) + " between " + Format(x.Lower) + " and " + Format(x.Upper), _ => expression.ToString() ?? string.Empty
        };
    }
}
