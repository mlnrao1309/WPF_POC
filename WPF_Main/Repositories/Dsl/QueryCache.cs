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

    /// <summary>Empties the cache. Not required in normal use — a different
    /// query text is a different key — but handy for a "clear cache" action
    /// or after the user edits a saved query in place.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _order.Clear();
        }
    }
}