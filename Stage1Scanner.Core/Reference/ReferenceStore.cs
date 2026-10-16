namespace Stage1Scanner.Core.Market;

/// <summary>
/// Per-instrument store of active references. Keys are case-insensitive IDs.
/// Parallel to CriticalStructureStore; the two coexist.
/// </summary>
public sealed class ReferenceStore
{
    private readonly Dictionary<string, Reference> _references =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<Reference> All => _references.Values;

    public void Set(Reference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _references[reference.Id] = reference;
    }

    public bool TryGet(string id, out Reference reference)
        => _references.TryGetValue(id, out reference!);

    public bool Clear(string id) => _references.Remove(id);

    public void ClearAll() => _references.Clear();
}