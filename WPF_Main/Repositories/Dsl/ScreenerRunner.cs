using AlgoTrading.DataAccess.Repositories;
using AlgoTrading.Screener.Repositories;
using Stage1Scanner.Core.Market;
using Stage1Scanner.Core.Pivots;

namespace AlgoTrading.Screener.Dsl;

public sealed class ScreenerRunner : IScreenerRunner
{
    private const int DefaultTake   = 1000;
    private const int PerBarHardCap = 10_000;

    private readonly IInstrumentRegistry _registry; // <--- Changed to Registry
    private readonly Dictionary<string, Func<IReadOnlyList<Series>, int, Series>> _functions;
    private readonly QueryCache _queryCache;

    public ScreenerRunner(IInstrumentRegistry registry, QueryCache queryCache)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

        // 1. Fetch ALL instruments from memory (Zero DB calls)
        var universe = _registry.GetAll();

        System.Diagnostics.Debug.WriteLine($"[Screener] In-Memory Universe={universe.Count}");
        if (universe.Count == 0) return Array.Empty<ScreenerRow>();

        var eval = new Evaluator(_functions);
        var results = new List<ScreenerRow>?[universe.Count];

        // 2. Setup Parallel Loop
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        // Note: We need to parse the requested Timeframe into the Core's Timeframe struct
        // Example: "60" -> new Timeframe(TimeframeUnit.Minute, 60)
        var coreTimeframe = ParseTimeframe(request.TimeFrame);

        try
        {
            Parallel.For(0, universe.Count, parallelOptions, i =>
            {
                var inst = universe.ElementAt(i); // Note: ElementAt on IReadOnlyCollection might be O(n), convert to List first in production

                // 3. Convert Core Candles -> Legacy Bars
                var bars = MapToBars(inst, coreTimeframe);
                if (bars.Count == 0) return;

                results[i] = EvaluateSymbol(query, inst.InstrumentToken, inst.Symbol, bars, eval, request.TimeFrame);
            });
        }
        catch (AggregateException agg)
        {
            var flat = agg.Flatten();
            if (flat.InnerExceptions.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(flat.InnerExceptions[0]).Throw();
            throw;
        }

        // 4. Flatten and Sort
        var output = new List<ScreenerRow>();
        foreach (var slot in results) { if (slot is not null) output.AddRange(slot); }

        if (query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<ScreenerRow>? ordered = null;
            IComparer<object?> cmp = NullsLastComparer.Instance;

            for (int i = 0; i < query.OrderBy.Count; i++)
            {
                int keyIndex = i;
                bool desc = query.OrderBy[i].Descending;
                Func<ScreenerRow, object?> key = r => keyIndex < r.OrderKeys.Length ? r.OrderKeys[keyIndex] : null;

                if (ordered is null) ordered = desc ? output.OrderByDescending(key, cmp) : output.OrderBy(key, cmp);
                else ordered = desc ? ordered.ThenByDescending(key, cmp) : ordered.ThenBy(key, cmp);
            }

            output = ordered!.ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
        }

        int effectiveTake = query.Take is int t && t > 0 ? t : DefaultTake;
        if (output.Count > effectiveTake) output = output.Take(effectiveTake).ToList();

        return output;
    }

    // =====================================================================
    // BRIDGE: Convert Core Objects (Candles/Pivots) -> Legacy Bar
    // =====================================================================
    private static List<Bar> MapToBars(Instruments inst, Timeframe tf)
    {
        // Check if the instrument has candles for the requested timeframe
        Candles candles;
        lock (inst)
        {
            if (!inst.TryGetCandles(tf, out candles)) return new List<Bar>();
        }
        // Candles is immutable, so conversion can finish after releasing the instrument lock.

        var bars = new List<Bar>(candles.Count);

        // Fetch latest Pivots from the in-memory Structures
        // NOTE: Because WarmupRepository overwrites keys like "D.R1", 
        // the store only holds the LATEST pivot. If you need historical pivots, 
        // the WarmupRepository needs to save them with unique IDs (e.g., "D.R1_20261016").
        decimal? pp = GetLevel(inst, "D.PP");
        decimal? r1 = GetLevel(inst, "D.R1");
        decimal? s1 = GetLevel(inst, "D.S1");
        decimal? wpp = GetLevel(inst, "W.PP");
        
        // Create the generic Pivots object to attach to all bars
        var activePivots = new Pivots(PP: pp, R1: r1, S1: s1, WeekPP: wpp);

        foreach (var c in candles)
        {
            bars.Add(new Bar(
                timeStamp: c.CloseTime.DateTime, // Convert DateTimeOffset to DateTime
                open: c.Open,
                high: c.High,
                low: c.Low,
                close: c.Close,
                volume: (long)c.Volume,
                oi: c.OpenInterest,
                pivots: activePivots
            ));
        }

        return bars;
    }

    private static decimal? GetLevel(Instruments inst, string id)
    {
        return inst.Structures.TryGetLevel(id, out var level) ? level.Price : null;
    }

    private static Timeframe ParseTimeframe(string tf)
    {
        // Basic parser: "60" -> Minute 60, "D" -> Day 1, "W" -> Week 1
        return tf.ToUpper() switch
        {
            "D" => new Timeframe(TimeframeUnit.Day, 1),
            "W" => new Timeframe(TimeframeUnit.Week, 1),
            _   => new Timeframe(TimeframeUnit.Minute, int.Parse(tf))
        };
    }

    // =====================================================================
    // Existing EvaluateSymbol (Slightly modified to take token/symbol directly)
    // =====================================================================
    private static List<ScreenerRow> EvaluateSymbol(
        QueryNode query, long token, string symbol, IReadOnlyList<Bar> bars, Evaluator eval, string timeframe)
    {
        int N = bars.Count;

        // 1. Pass token & symbol instead of InstrumentRef
        var fields = BuildFields(token, symbol, bars);

        var env = new EvalEnv(N);
        env.Bind(query.SourceVar, new SymbolValue(symbol, token, fields));

        foreach (var l in query.Lets) { var v = eval.Eval(l.Value, env); env.Bind(l.Name, v); }

        Series? pred = query.Where is null ? null : eval.EvalSeries(query.Where, env);
        var rows = new List<ScreenerRow>(1);

        IReadOnlyList<string>? projectedColumns = query.Select.Count == 0 ? null : query.Select.Select(si => si.Alias ?? DefaultName(si.Value)).ToList();

        if (query.PerBar)
        {
            for (int barIndex = 0; barIndex < N; barIndex++)
            {
                if (pred is null || pred.AsBool(barIndex) != true) continue;

                rows.Add(new ScreenerRow
                {
                    InstrumentToken = token,
                    Symbol = symbol,
                    Segment = "",
                    Exchange = "",
                    TimeFrame = timeframe,
                    LastBarTime = bars[barIndex].TimeStamp,
                    // 2. Pass token & symbol to Project
                    Values = Project(query, eval, env, bars, barIndex, token, symbol),
                    OrderKeys = EvalOrderKeys(query, eval, env, barIndex),
                    ProjectedColumns = projectedColumns
                });
                if (rows.Count >= PerBarHardCap) break;
            }
        }
        else
        {
            int last = N - 1;
            if (pred is not null && pred.AsBool(last) != true) return rows;

            rows.Add(new ScreenerRow
            {
                InstrumentToken = token,
                Symbol = symbol,
                Segment = "",
                Exchange = "",
                TimeFrame = timeframe,
                LastBarTime = bars[last].TimeStamp,
                Values = Project(query, eval, env, bars, last, token, symbol),
                OrderKeys = EvalOrderKeys(query, eval, env, last),
                ProjectedColumns = projectedColumns
            });
        }
        return rows;
    }
    private static Dictionary<string, object?> Project(
    QueryNode query,
    Evaluator eval,
    EvalEnv env,
    IReadOnlyList<Bar> bars,
    int barIndex,
    long token,
    string symbol)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (query.Select.Count == 0)
        {
            // Default projection when user does not specify a select
            values["Symbol"] = symbol;
            values["InstrumentToken"] = token;
            values["Close"] = bars[barIndex].Close;
            values["Volume"] = bars[barIndex].Volume;
        }
        else
        {
            foreach (var item in query.Select)
            {
                var s = eval.EvalSeries(item.Value, env);
                var name = item.Alias ?? DefaultName(item.Value);
                values[name] = barIndex >= 0 && barIndex < s.Length
                    ? s.Values[barIndex]
                    : null;
            }
        }

        return values;
    }


    private static Dictionary<string, Series> BuildFields(long token, string symbol, IReadOnlyList<Bar> bars)
    {
        int N = bars.Count;
        return new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase)
        {
            ["Open"] = Series.Num(bars.Select(b => (decimal?)b.Open).ToArray()),
            ["High"] = Series.Num(bars.Select(b => (decimal?)b.High).ToArray()),
            ["Low"] = Series.Num(bars.Select(b => (decimal?)b.Low).ToArray()),
            ["Close"] = Series.Num(bars.Select(b => (decimal?)b.Close).ToArray()),
            ["Volume"] = Series.Num(bars.Select(b => (decimal?)b.Volume).ToArray()),
            ["OI"] = Series.Num(bars.Select(b => b.OI).ToArray()),
            ["Time"] = Series.Text(bars.Select(b =>
                            (string?)b.TimeStamp.ToString("yyyy-MM-dd HH:mm")).ToArray()),

            ["Hour"] = Series.Num(bars.Select(b => (decimal?)b.TimeStamp.Hour).ToArray()),
            ["Minute"] = Series.Num(bars.Select(b => (decimal?)b.TimeStamp.Minute).ToArray()),
            ["SessionId"] = Series.Num(bars.Select(b =>
                                (decimal?)b.TimeStamp.Date.Ticks).ToArray()),

            ["InstrumentToken"] = Series.Num(Enumerable.Repeat((long?)token, N).ToArray()),
            ["Symbol"] = Series.Constant(symbol, N),

            // Hardcoded to empty since Stage1Scanner.Core's Instruments class doesn't hold these
            ["Segment"] = Series.Constant("", N),
            ["Exchange"] = Series.Constant("", N),

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

            // ---- trap flags ----
            ["PivotPPR1"] = Series.Num(bars.Select(b => b.Pivots.PPR1 ? 1m : 0m).ToArray()),
            ["PivotPPS1"] = Series.Num(bars.Select(b => b.Pivots.PPS1 ? 1m : 0m).ToArray()),
            ["PivotWeekPPR1"] = Series.Num(bars.Select(b => b.Pivots.WeekPPR1 ? 1m : 0m).ToArray()),
            ["PivotWeekPPS1"] = Series.Num(bars.Select(b => b.Pivots.WeekPPS1 ? 1m : 0m).ToArray()),
        };
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
        Ident i => i.Name,
        Member m => m.Name,
        Call c => c.Name,
        _ => "value"
    };


    // (BuildFields, Project, EvalOrderKeys, DefaultName remain exactly as they were previously)
    // You can copy them directly from your current file, just change the signature of BuildFields 
    // to accept (long token, string symbol, IReadOnlyList<Bar> bars) instead of InstrumentRef.
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
