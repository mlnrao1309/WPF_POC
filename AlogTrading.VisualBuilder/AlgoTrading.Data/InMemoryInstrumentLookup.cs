using System.Diagnostics.CodeAnalysis;

namespace AlgoTrading.Data;

public sealed class InMemoryInstrumentLookup : IInstrumentLookup
{
    private readonly Dictionary<uint, InstrumentInfo> _map;

    public InMemoryInstrumentLookup(IEnumerable<InstrumentInfo> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        _map = new Dictionary<uint, InstrumentInfo>();
        foreach (var i in instruments) _map[i.Token] = i;
    }

    public bool TryGet(uint token, [NotNullWhen(true)] out InstrumentInfo info)
    {
        if (_map.TryGetValue(token, out var found)) { info = found; return true; }
        info = null!;
        return false;
    }
}