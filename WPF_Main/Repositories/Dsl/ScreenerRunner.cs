using AlgoTrading.Screener.Repositories;

namespace AlgoTrading.Screener.Dsl;

public sealed class ScreenerRunner : IScreenerRunner
{
    private const int DefaultTake   = 1000;
    private const int PerBarHardCap = 10_000;

    private readonly IScreenerRepository _repo;
    private readonly Dictionary<string, Func<IReadOnlyList<Series>, int, Series>> _functions;

    private readonly QueryCache _queryCache;

    public ScreenerRunner(IScreenerRepository repo, QueryCache queryCache)
    {
        _repo = repo;
        _functions = Functions.Build();
        _queryCache = queryCache ?? throw new ArgumentNullException(nameof(queryCache));
    }
    public async Task<IReadOnlyList<ScreenerRow>> RunAsync(
        string source,
        ScreenerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Query is empty.", nameof(source));
        var query = _queryCache.GetOrParse(source);
        int? maxBarsPerInstrument = QueryHistoryRequirements.TryGetFiniteBars(query, out int requiredBars)
            ? requiredBars
            : null;
        //var query = Parser.Parse(source);

        System.Diagnostics.Debug.WriteLine(
            $"[Screener] tf='{request.TimeFrame}' from={request.FromInclusive:o} " +
            $"to={request.ToExclusive:o} seg='{request.Segment}' exch='{request.Exchange}' " +
            $"perBar={query.PerBar} take={query.Take?.ToString() ?? "(default)"}");

        var universe = await _repo.GetUniverseAsync(
            request.TimeFrame,
            request.FromInclusive,
            request.ToExclusive,
            request.Segment,
            request.Exchange,
            cancellationToken);

        System.Diagnostics.Debug.WriteLine($"[Screener] universe={universe.Count}");
        if (universe.Count == 0) return Array.Empty<ScreenerRow>();

        var tokens      = universe.Select(u => u.InstrumentToken).ToArray();
        var barsByToken = await _repo.GetBarsBulkAsync(
            tokens, request.TimeFrame,
            request.FromInclusive, request.ToExclusive, maxBarsPerInstrument, cancellationToken);

        System.Diagnostics.Debug.WriteLine(
            $"[Screener] symbolsWithBars={barsByToken.Count} " +
            $"totalBars={barsByToken.Values.Sum(v => v.Count)}");

        // ---------- Parallel per-symbol evaluation ----------
        var results = new List<ScreenerRow>?[universe.Count];

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        var eval = new Evaluator(_functions);

        try
        {
            Parallel.For(0, universe.Count, parallelOptions, i =>
            {
                var inst = universe[i];

                if (!barsByToken.TryGetValue(inst.InstrumentToken, out var bars) || bars.Count == 0)
                    return;

                results[i] = EvaluateSymbol(query, inst, bars, eval, request.TimeFrame);
            });
        }
        catch (AggregateException agg)
        {
            var flat = agg.Flatten();
            if (flat.InnerExceptions.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(flat.InnerExceptions[0]).Throw();
            throw;
        }

        // ---------- Flatten in universe order ----------
        var output = new List<ScreenerRow>();
        foreach (var slot in results)
        {
            if (slot is null) continue;
            output.AddRange(slot);
        }

        // ---------- orderby ----------
        if (query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<ScreenerRow>? ordered = null;
            IComparer<object?> cmp = NullsLastComparer.Instance;

            for (int i = 0; i < query.OrderBy.Count; i++)
            {
                int keyIndex = i;
                bool desc    = query.OrderBy[i].Descending;

                Func<ScreenerRow, object?> key =
                    r => keyIndex < r.OrderKeys.Length ? r.OrderKeys[keyIndex] : null;

                if (ordered is null)
                    ordered = desc ? output.OrderByDescending(key, cmp)
                                   : output.OrderBy(key, cmp);
                else
                    ordered = desc ? ordered.ThenByDescending(key, cmp)
                                   : ordered.ThenBy(key, cmp);
            }

            // Tie-break on Symbol so repeated runs of the same query with
            // equal sort keys always produce identical ordering. Without
            // this, Parallel.For's flattening order can differ between runs
            // when two symbols share the primary sort value.
            output = ordered!
                .ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        int effectiveTake = query.Take is int t && t > 0 ? t : DefaultTake;
        if (output.Count > effectiveTake)
            output = output.Take(effectiveTake).ToList();

        return output;
    }

    // =====================================================================
    // Per-symbol evaluation — thread-local, no shared mutable state
    // =====================================================================

    private static List<ScreenerRow> EvaluateSymbol(
        QueryNode query,
        InstrumentRef inst,
        IReadOnlyList<Bar> bars,
        Evaluator eval,
        string timeframe)
    {
        int N = bars.Count;
        var fields = BuildFields(inst, bars);

        var env = new EvalEnv(N);
        env.Bind(query.SourceVar,
            new SymbolValue(inst.TradingSymbol, inst.InstrumentToken, fields));

        foreach (var l in query.Lets)
        {
            var v = eval.Eval(l.Value, env);
            env.Bind(l.Name, v);
        }

        Series? pred = query.Where is null
            ? null
            : eval.EvalSeries(query.Where, env);

        var rows = new List<ScreenerRow>(1);

        IReadOnlyList<string>? projectedColumns = query.Select.Count == 0
            ? null
            : query.Select.Select(si => si.Alias ?? DefaultName(si.Value)).ToList();

        if (query.PerBar)
        {
            for (int barIndex = 0; barIndex < N; barIndex++)
            {
                if (pred is null || pred.AsBool(barIndex) != true) continue;

                var values    = Project(query, eval, env, bars, barIndex, inst);
                var orderKeys = EvalOrderKeys(query, eval, env, barIndex);

                rows.Add(new ScreenerRow
                {
                    InstrumentToken  = inst.InstrumentToken,
                    Symbol           = inst.TradingSymbol,
                    Segment          = inst.Segment  ?? string.Empty,
                    Exchange         = inst.Exchange ?? string.Empty,
                    TimeFrame        = timeframe,
                    LastBarTime      = bars[barIndex].TimeStamp,
                    Values           = values,
                    OrderKeys        = orderKeys,
                    ProjectedColumns = projectedColumns
                });

                if (rows.Count >= PerBarHardCap) break;
            }
        }
        else
        {
            int last = N - 1;
            if (pred is not null && pred.AsBool(last) != true)
                return rows;

            var values    = Project(query, eval, env, bars, last, inst);
            var orderKeys = EvalOrderKeys(query, eval, env, last);

            rows.Add(new ScreenerRow
            {
                InstrumentToken  = inst.InstrumentToken,
                Symbol           = inst.TradingSymbol,
                Segment          = inst.Segment  ?? string.Empty,
                Exchange         = inst.Exchange ?? string.Empty,
                TimeFrame        = timeframe,
                LastBarTime      = bars[last].TimeStamp,
                Values           = values,
                OrderKeys        = orderKeys,
                ProjectedColumns = projectedColumns
            });
        }

        return rows;
    }

    // =====================================================================
    // Field dictionary
    // =====================================================================

    private static Dictionary<string, Series> BuildFields(InstrumentRef inst, IReadOnlyList<Bar> bars)
    {
        int N = bars.Count;
        return new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"]   = Series.Num(bars.Select(b => (decimal?)b.Open).ToArray()),
            ["High"]   = Series.Num(bars.Select(b => (decimal?)b.High).ToArray()),
            ["Low"]    = Series.Num(bars.Select(b => (decimal?)b.Low).ToArray()),
            ["Close"]  = Series.Num(bars.Select(b => (decimal?)b.Close).ToArray()),
            ["Volume"] = Series.Num(bars.Select(b => (long?)b.Volume).ToArray()),
            ["OI"]     = Series.Num(bars.Select(b => b.OI).ToArray()),
            ["Time"]   = Series.Text(bars.Select(b =>
                            (string?)b.TimeStamp.ToString("yyyy-MM-dd HH:mm")).ToArray()),

            ["Hour"]      = Series.Num(bars.Select(b => (decimal?)b.TimeStamp.Hour).ToArray()),
            ["Minute"]    = Series.Num(bars.Select(b => (decimal?)b.TimeStamp.Minute).ToArray()),
            ["SessionId"] = Series.Num(bars.Select(b =>
                                (decimal?)b.TimeStamp.Date.Ticks).ToArray()),

            ["InstrumentToken"] = Series.Num(
                Enumerable.Repeat((long?)inst.InstrumentToken, N).ToArray()),
            ["Symbol"]   = Series.Constant(inst.TradingSymbol, N),
            ["Segment"]  = Series.Constant(inst.Segment  ?? string.Empty, N),
            ["Exchange"] = Series.Constant(inst.Exchange ?? string.Empty, N),

            // ---- daily pivots ----
            ["PivotPP"] = Series.Num(bars.Select(b => b.Pivots.PP).ToArray()),
            ["PivotR1"] = Series.Num(bars.Select(b => b.Pivots.R1).ToArray()),
            ["PivotR2"] = Series.Num(bars.Select(b => b.Pivots.R2).ToArray()),
            ["PivotR3"] = Series.Num(bars.Select(b => b.Pivots.R3).ToArray()),
            ["PivotR4"] = Series.Num(bars.Select(b => b.Pivots.R4).ToArray()),
            ["PivotR5"] = Series.Num(bars.Select(b => b.Pivots.R5).ToArray()),
            ["PivotS1"] = Series.Num(bars.Select(b => b.Pivots.S1).ToArray()),
            ["PivotS2"] = Series.Num(bars.Select(b => b.Pivots.S2).ToArray()),
            ["PivotS3"] = Series.Num(bars.Select(b => b.Pivots.S3).ToArray()),
            ["PivotS4"] = Series.Num(bars.Select(b => b.Pivots.S4).ToArray()),
            ["PivotS5"] = Series.Num(bars.Select(b => b.Pivots.S5).ToArray()),

            // ---- weekly pivots ----
            ["PivotWeekPP"] = Series.Num(bars.Select(b => b.Pivots.WeekPP).ToArray()),
            ["PivotWeekR1"] = Series.Num(bars.Select(b => b.Pivots.WeekR1).ToArray()),
            ["PivotWeekR2"] = Series.Num(bars.Select(b => b.Pivots.WeekR2).ToArray()),
            ["PivotWeekR3"] = Series.Num(bars.Select(b => b.Pivots.WeekR3).ToArray()),
            ["PivotWeekR4"] = Series.Num(bars.Select(b => b.Pivots.WeekR4).ToArray()),
            ["PivotWeekR5"] = Series.Num(bars.Select(b => b.Pivots.WeekR5).ToArray()),
            ["PivotWeekS1"] = Series.Num(bars.Select(b => b.Pivots.WeekS1).ToArray()),
            ["PivotWeekS2"] = Series.Num(bars.Select(b => b.Pivots.WeekS2).ToArray()),
            ["PivotWeekS3"] = Series.Num(bars.Select(b => b.Pivots.WeekS3).ToArray()),
            ["PivotWeekS4"] = Series.Num(bars.Select(b => b.Pivots.WeekS4).ToArray()),
            ["PivotWeekS5"] = Series.Num(bars.Select(b => b.Pivots.WeekS5).ToArray()),

            // ---- trap flags (exposed as 0/1 numeric series) ----
            ["PivotPPR1"]     = Series.Num(bars.Select(b => b.Pivots.PPR1     ? 1m : 0m).ToArray()),
            ["PivotPPS1"]     = Series.Num(bars.Select(b => b.Pivots.PPS1     ? 1m : 0m).ToArray()),
            ["PivotWeekPPR1"] = Series.Num(bars.Select(b => b.Pivots.WeekPPR1 ? 1m : 0m).ToArray()),
            ["PivotWeekPPS1"] = Series.Num(bars.Select(b => b.Pivots.WeekPPS1 ? 1m : 0m).ToArray()),
        };
    }

    private static Dictionary<string, object?> Project(
        QueryNode query,
        Evaluator eval,
        EvalEnv env,
        IReadOnlyList<Bar> bars,
        int barIndex,
        InstrumentRef inst)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (query.Select.Count == 0)
        {
            values["Symbol"] = inst.TradingSymbol;
            values["Close"]  = bars[barIndex].Close;
            values["Volume"] = bars[barIndex].Volume;
        }
        else
        {
            foreach (var item in query.Select)
            {
                var s    = eval.EvalSeries(item.Value, env);
                var name = item.Alias ?? DefaultName(item.Value);
                values[name] = barIndex >= 0 && barIndex < s.Length
                    ? s.Values[barIndex]
                    : null;
            }
        }

        return values;
    }

    private static object?[] EvalOrderKeys(
        QueryNode query, Evaluator eval, EvalEnv env, int barIndex)
    {
        if (query.OrderBy.Count == 0) return Array.Empty<object?>();

        var keys = new object?[query.OrderBy.Count];
        for (int i = 0; i < query.OrderBy.Count; i++)
        {
            var s = eval.EvalSeries(query.OrderBy[i].Value, env);
            keys[i] = barIndex >= 0 && barIndex < s.Length
                ? s.Values[barIndex]
                : null;
        }
        return keys;
    }

    private static string DefaultName(Expr e) => e switch
    {
        Ident  i => i.Name,
        Member m => m.Name,
        Call   c => c.Name,
        _        => "value"
    };
}

public sealed class ScreenerRow
{
    public long InstrumentToken { get; init; }
    public required string Symbol { get; init; }
    public required string Segment { get; init; }
    public required string Exchange { get; init; }
    public required string TimeFrame { get; init; }
    public DateTime LastBarTime { get; init; }
    public required Dictionary<string, object?> Values { get; init; }

    /// <summary>
    /// Positional sort keys: OrderKeys[i] corresponds to QueryNode.OrderBy[i].
    /// Internal so it never leaks into select output or the DataGrid.
    /// </summary>
    internal object?[] OrderKeys { get; init; } = Array.Empty<object?>();

    /// <summary>
    /// Resolved column names from the select clause, in order. Null or empty
    /// means the query had no select, so the grid uses its default column set.
    /// </summary>
    public IReadOnlyList<string>? ProjectedColumns { get; init; }
}
