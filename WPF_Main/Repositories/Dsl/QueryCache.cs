using System.IO;

namespace AlgoTrading.Screener.Dsl;

/// <summary>
/// Caches parsed queries keyed on the raw query text. QueryNode is
/// immutable (all records), so sharing across threads and runs is safe.
///
/// No TTL: parsing is deterministic for a given grammar version. When the
/// grammar changes, the app is rebuilt and the cache starts empty.
/// </summary>
public sealed class QueryCache
{
    private const int MaxEntries = 200;

    private readonly object _gate = new();
    private readonly Dictionary<string, QueryNode> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<QueryNode,
        System.Collections.Concurrent.ConcurrentDictionary<(long Token, string Timeframe), IndicatorEvaluationCache>> _indicators = new();

    internal IndicatorEvaluationCache GetIndicators(QueryNode query, long token, string timeframe)
        => _indicators.GetOrCreateValue(query).GetOrAdd((token, timeframe), _ => new IndicatorEvaluationCache());

    /// <summary>Returns the cached parse, or parses and caches on miss.</summary>
    public QueryNode GetOrParse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Query is empty.", nameof(source));

        lock (_gate)
        {
            if (_cache.TryGetValue(source, out var cached))
                return cached;
        }

        // Parse outside the lock so a slow parse doesn't block other lookups.
        var parsed = Parser.Parse(source);

        lock (_gate)
        {
            if (_cache.Count >= MaxEntries && _order.Count > 0)
            {
                var oldest = _order.Dequeue();
                _cache.Remove(oldest);
            }

            _cache[source] = parsed;
            _order.Enqueue(source);
        }

        return parsed;
    }

    /// <summary>Refreshes evaluated screeners against final candles and retains their calculation results.</summary>
    public async Task SaveCalculationsAsync(string path, AlgoTrading.DataAccess.Repositories.IInstrumentRegistry registry, Guid? generation = null)
    {
        KeyValuePair<string, QueryNode>[] queries;
        lock (_gate) queries = _cache.ToArray();
        foreach (var query in queries)
        {
            if (!_indicators.TryGetValue(query.Value, out var contexts)) continue;
            foreach (string timeframe in contexts.Keys.Select(key => key.Timeframe).Distinct().ToArray())
            {
                string requested = timeframe.EndsWith('m') ? timeframe[..^1] : timeframe;
                await new ScreenerRunner(registry, this).RunAsync(query.Key, new ScreenerRequest { TimeFrame = requested });
            }
        }
        var calculations = new List<object>();
        foreach (var query in queries)
        {
            if (!_indicators.TryGetValue(query.Value, out var contexts)) continue;
            foreach (var context in contexts)
                calculations.Add(new { Query = query.Key, InstrumentToken = context.Key.Token,
                    Timeframe = context.Key.Timeframe, Functions = context.Value.Export() });
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await System.Text.Json.JsonSerializer.SerializeAsync(stream,
                    new { FormatVersion = 2, Generation = generation, SavedAt = DateTimeOffset.UtcNow, Calculations = calculations });
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task LoadCalculationsAsync(string path, Guid expectedGeneration)
    {
        if (!File.Exists(path)) return;
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            int version = root.GetProperty("FormatVersion").GetInt32();
            if (version is not (1 or 2)) throw new InvalidDataException("Unsupported calculation file version.");
            if (version == 2 && root.GetProperty("Generation").GetGuid() != expectedGeneration)
                throw new InvalidDataException("The calculations belong to a different snapshot generation.");
            foreach (var calculation in root.GetProperty("Calculations").EnumerateArray())
            {
                var query = GetOrParse(calculation.GetProperty("Query").GetString()!);
                long token = calculation.GetProperty("InstrumentToken").GetInt64();
                string timeframe = calculation.GetProperty("Timeframe").GetString()!;
                var restored = new IndicatorEvaluationCache();
                restored.Restore(calculation.GetProperty("Functions"));
                _indicators.GetOrCreateValue(query)[(token, timeframe)] = restored;
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException
            or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or IOException
            or ArithmeticException or InvalidCastException)
        {
            // History remains available; invalid calculation caches are rebuilt on demand.
            Clear();
            System.Diagnostics.Trace.TraceWarning("Saved calculations were not restored: {0}", exception.Message);
        }
    }

    /// <summary>Empties the cache. Not required in normal use — a different
    /// query text is a different key — but handy for a "clear cache" action
    /// or after the user edits a saved query in place.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _indicators.Clear();
            _order.Clear();
        }
    }
}