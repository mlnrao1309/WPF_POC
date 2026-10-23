using System.IO;

namespace AlgoTrading.Screener.Dsl;

/// <summary>Continues function results for appended bars; corrected inputs rebuild their state.</summary>
public sealed class IndicatorEvaluationCache
{
    private readonly object _gate = new();
    private readonly Dictionary<Call, Entry> _calls = new();
    private readonly List<(string Name, Entry Value)> _restored = new();

    public Series Evaluate(Call call, IReadOnlyList<Series> args, Func<IReadOnlyList<Series>, int, Series> calculate)
    {
        lock (_gate)
        {
            string name = call.Name.ToUpperInvariant();
            int n = args[0].Length;
            int inputs = name switch
            {
                "ATR" or "SUPERTREND" => 3,
                "SESSIONOPEN" or "SESSIONCLOSE" => 2,
                _ => 1
            };
            if (!_calls.TryGetValue(call, out var entry) || !entry.CanContinue(args, inputs))
            {
                int saved = _restored.FindIndex(item => item.Name == name && item.Value.CanContinue(args, inputs));
                if (saved >= 0)
                {
                    entry = _restored[saved].Value;
                    _restored.RemoveAt(saved);
                }
                else entry = new Entry(name, args, inputs, calculate);
                // Failed calculations must not leave partially advanced state in the cache.
                _calls.Remove(call);
            }
            entry.Args = args;
            try
            {
                int start = entry.Values.Count;
                // This helper intentionally revises the formerly last bar when a new bar arrives.
                if (name == "ISLASTBAROFDAY" && n > start && start > 0)
                    entry.Values[start - 1] = args[0].AsNum(start - 1) is not decimal current ? null
                        : args[0].AsNum(start) is not decimal next || current != next;
                for (int i = start; i < n; i++) entry.Values.Add(entry.Next(i));
                entry.Remember(args, inputs);
                _calls[call] = entry;
                return new Series(entry.Values.ToArray());
            }
            catch
            {
                _calls.Remove(call);
                throw;
            }
        }
    }

    public object[] Export()
    {
        lock (_gate)
            return _calls.Select(pair => (object)new { Function = pair.Key.Name,
                Arguments = pair.Value.Args.Select(argument => argument.Values.ToArray()).ToArray(),
                Values = pair.Value.Values.ToArray(), State = pair.Value.ExportState() }).ToArray();
    }

    /// <summary>Restores saved outputs and continuation state; input matching happens before reuse.</summary>
    public void Restore(System.Text.Json.JsonElement functions)
    {
        var registered = Functions.Build();
        var restored = new List<(string Name, Entry Value)>();
        foreach (var function in functions.EnumerateArray())
        {
            string name = (function.GetProperty("Function").GetString() ?? throw new InvalidDataException("Saved function name is missing.")).ToUpperInvariant();
            if (!registered.TryGetValue(name, out var calculate)) continue;
            var arguments = function.GetProperty("Arguments").EnumerateArray()
                .Select(argument => new Series(argument.EnumerateArray().Select(ReadValue).ToArray())).ToArray();
            var values = function.GetProperty("Values").EnumerateArray().Select(ReadValue).ToArray();
            if (arguments.Length == 0 || arguments.Any(argument => argument.Length != values.Length))
                throw new InvalidDataException("Saved calculation inputs and outputs have different lengths.");
            int inputs = name is "ATR" or "SUPERTREND" ? 3 : name is "SESSIONOPEN" or "SESSIONCLOSE" ? 2 : 1;
            var entry = new Entry(name, arguments, inputs, calculate);
            if (function.TryGetProperty("State", out var state)) entry.RestoreState(state);
            else
            {
                // Older result-only files need one replay to recover hidden recursive state.
                for (int i = 0; i < values.Length; i++) entry.Next(i);
            }
            entry.Values.AddRange(values);
            entry.Remember(arguments, inputs);
            restored.Add((name, entry));
        }
        lock (_gate) _restored.AddRange(restored);
    }

    private static object? ReadValue(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null => null,
        System.Text.Json.JsonValueKind.Number => value.GetDecimal(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => value.GetString(),
        _ => throw new InvalidDataException("Unsupported saved calculation value.")
    };

    private sealed class Entry
    {
        public IReadOnlyList<Series> Args;
        public readonly List<object?> Values = new();
        public readonly Func<int, object?> Next;
        private object?[][] _history = [];
        private object?[] _parameters = [];
        private readonly Smoother _first = new(), _second = new(), _third = new();
        private decimal _sum, _other, _upper, _lower;
        private int _count;
        private bool _upward;
        private decimal? _session;
        private object? _held, _last;

        public Entry(string name, IReadOnlyList<Series> args, int inputs,
            Func<IReadOnlyList<Series>, int, Series> calculate)
        {
            Args = args;
            int fallback = name is "RSI" or "ATR" ? 14 : name == "SUPERTREND" ? 10 : 0;
            int period = args.Count > inputs ? Indicators.IntArg(args[inputs], args[0].Length, fallback) : fallback;
            decimal? Num(int a, int i) => Args[a].AsNum(i);
            decimal? Range(int i)
            {
                var h = Num(0, i); var l = Num(1, i);
                if (!h.HasValue || !l.HasValue) return null;
                decimal range = h.Value - l.Value;
                var previous = i == 0 ? null : Num(2, i - 1);
                return previous.HasValue ? Math.Max(range, Math.Max(Math.Abs(h.Value - previous.Value),
                    Math.Abs(l.Value - previous.Value))) : range;
            }

            Next = name switch
            {
                "EMA" => i => _first.Add(Num(0, i), period, false),
                "MACD" or "MACDSIGNAL" or "MACDHIST" => i =>
                {
                    var fast = _first.Add(Num(0, i), Indicators.IntArg(Args[1], Args[0].Length, 12), false);
                    var slow = _second.Add(Num(0, i), Indicators.IntArg(Args[2], Args[0].Length, 26), false);
                    decimal? line = fast - slow;
                    if (name == "MACD") return line;
                    var signal = _third.Add(line, Indicators.IntArg(Args[3], Args[0].Length, 9), false);
                    return name == "MACDSIGNAL" ? signal : line - signal;
                },
                "RSI" => i =>
                {
                    if (i == 0 || Num(0, i) is not decimal current || Num(0, i - 1) is not decimal previous)
                        return null;
                    decimal delta = current - previous;
                    var gain = _first.Add(Math.Max(delta, 0), period, true);
                    var loss = _second.Add(Math.Max(-delta, 0), period, true);
                    return !gain.HasValue || !loss.HasValue || gain == 0 && loss == 0 ? null
                        : loss == 0 ? 100m : 100m - 100m / (1m + gain.Value / loss.Value);
                },
                "ATR" => i => _first.Add(Range(i), period, true),
                "SUPERTREND" => i =>
                {
                    if (period <= 0 || Num(0, i) is not decimal high || Num(1, i) is not decimal low
                        || Num(2, i) is not decimal close) return null;
                    decimal range = Range(i)!.Value;
                    if (i < period)
                    {
                        _sum += range;
                        if (i < period - 1) return null;
                        _other = _sum / period;
                    }
                    else _other = (_other * (period - 1) + range) / period;
                    decimal factor = Args.Count > 4 ? Args[4].LastNum() ?? 3m : 3m;
                    decimal newUpper = (high + low) / 2m + factor * _other;
                    decimal newLower = (high + low) / 2m - factor * _other;
                    if (i > period - 1)
                    {
                        if (Num(2, i - 1) is not decimal previous) return null;
                        newUpper = newUpper < _upper || previous > _upper ? newUpper : _upper;
                        newLower = newLower > _lower || previous < _lower ? newLower : _lower;
                        _upward = _upward ? close >= newLower : close > newUpper;
                    }
                    _upper = newUpper; _lower = newLower;
                    return _upward ? _lower : _upper;
                },
                "SMA" or "AVG" or "BBMIDDLE" or "SUM" => i =>
                {
                    if (period <= 0) return null;
                    if (Num(0, i) is not decimal value) { _sum = 0; _count = 0; return null; }
                    _sum += value; _count++;
                    if (_count > period) { _sum -= Num(0, i - period) ?? 0m; _count = period; }
                    return _count == period ? name == "SUM" ? _sum : _sum / period : null;
                },
                "COUNTSTREAK" => i => (decimal)(_count = Args[0].AsBool(i) == true ? _count + 1 : 0),
                "BARSSINCE" or "BARSAGOFIRST" => i =>
                {
                    // _count is one-based so zero denotes no match.
                    if (Args[0].AsBool(i) == true && (name == "BARSSINCE" || _count == 0)) _count = i + 1;
                    return _count == 0 ? null : (decimal?)(i + 1 - _count);
                },
                "ISLASTBAROFDAY" => i => Num(0, i) is not decimal current ? null
                    : i == Args[0].Length - 1 || Num(0, i + 1) is not decimal next || current != next,
                "SESSIONOPEN" or "SESSIONCLOSE" => i =>
                {
                    if (Num(1, i) is not decimal current) return null;
                    if (!_session.HasValue || _session.Value != current)
                    {
                        _held = name == "SESSIONOPEN" ? Args[0].Values[i] : _last;
                        _session = current; _last = null;
                    }
                    if (name == "SESSIONCLOSE" && Args[0].Values[i] != null) _last = Args[0].Values[i];
                    return _held;
                },
                // Finite-window and scalar functions use the existing formulas on only their required tail.
                _ => i =>
                {
                    int lookback = name switch
                    {
                        "WMA" or "STDDEV" or "BBUPPER" or "BBLOWER" or "HIGHEST" or "LOWEST"
                            or "MIN" or "MAX" or "COUNT" or "ANY" or "ALL" => Math.Max(0, period - 1),
                        "ROC" => Math.Max(0, period),
                        "ISFIRSTBAROFDAY" or "ISLASTBAROFDAY" => 1,
                        "ABS" or "SQRT" or "LOG" or "EXP" or "ROUND" or "FLOOR" or "CEILING" => 0,
                        _ => i // Newly registered functions retain correctness until a continuation is supplied.
                    };
                    int start = Math.Max(0, i - lookback);
                    var tail = Args.Select(a => new Series(a.Values[start..(i + 1)])).ToArray();
                    return calculate(tail, i - start + 1).Last;
                }
            };
        }

        public object ExportState() => new
        {
            First = _first.Export(), Second = _second.Export(), Third = _third.Export(),
            Sum = _sum, Other = _other, Upper = _upper, Lower = _lower, Count = _count,
            Upward = _upward, Session = _session, Held = _held, Last = _last
        };

        public void RestoreState(System.Text.Json.JsonElement state)
        {
            _first.Restore(state.GetProperty("First"));
            _second.Restore(state.GetProperty("Second"));
            _third.Restore(state.GetProperty("Third"));
            _sum = state.GetProperty("Sum").GetDecimal();
            _other = state.GetProperty("Other").GetDecimal();
            _upper = state.GetProperty("Upper").GetDecimal();
            _lower = state.GetProperty("Lower").GetDecimal();
            _count = state.GetProperty("Count").GetInt32();
            _upward = state.GetProperty("Upward").GetBoolean();
            _session = (decimal?)ReadValue(state.GetProperty("Session"));
            _held = ReadValue(state.GetProperty("Held"));
            _last = ReadValue(state.GetProperty("Last"));
        }

        public bool CanContinue(IReadOnlyList<Series> args, int inputs)
        {
            if (args.Count != _history.Length || args[0].Length < Values.Count) return false;
            for (int a = 0; a < args.Count; a++)
            {
                if (a >= inputs && !Equals(_parameters[a], args[a].Last)) return false;
                for (int i = 0; i < Values.Count; i++)
                    if (!Equals(_history[a][i], args[a].Values[i])) return false;
            }
            return true;
        }

        public void Remember(IReadOnlyList<Series> args, int inputs)
        {
            _history = args.Select(a => a.Values.ToArray()).ToArray();
            _parameters = args.Select(a => a.Last).ToArray();
        }
    }

    private sealed class Smoother
    {
        private int _count;
        private decimal _sum;
        private decimal? _previous;
        public object Export() => new { Count = _count, Sum = _sum, Previous = _previous };
        public void Restore(System.Text.Json.JsonElement state)
        {
            _count = state.GetProperty("Count").GetInt32();
            _sum = state.GetProperty("Sum").GetDecimal();
            _previous = (decimal?)ReadValue(state.GetProperty("Previous"));
            if (_count < 0) throw new InvalidDataException("Invalid smoothing state.");
        }
        public decimal? Add(decimal? value, int period, bool wilder)
        {
            if (period <= 0 || !value.HasValue) return null;
            if (!_previous.HasValue)
            {
                _sum += value.Value;
                if (++_count == period) _previous = _sum / period;
            }
            else if (wilder) _previous = (_previous.Value * (period - 1) + value.Value) / period;
            else
            {
                decimal alpha = 2m / (period + 1m);
                _previous = value.Value * alpha + _previous.Value * (1m - alpha);
            }
            return _previous;
        }
    }
}
