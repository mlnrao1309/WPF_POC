// Dsl/EvalEnv.cs
namespace AlgoTrading.Screener.Dsl;

public sealed class EvalEnv
{
    private readonly Dictionary<string, object> _vars = new(StringComparer.OrdinalIgnoreCase);

    public int Length { get; }
    public EvalEnv(int length) => Length = length;

    public void Bind(string name, object value) => _vars[name] = value;

    public object Resolve(string name) =>
        _vars.TryGetValue(name, out var v)
            ? v
            : throw new InvalidOperationException($"Unknown identifier '{name}'.");
}