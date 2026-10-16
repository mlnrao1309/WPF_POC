namespace Stage1Scanner.Core.Market;

public enum CriticalGeometry
{
    Level,
    Zone
}

public enum CriticalRole
{
    Support,
    Resistance,
    Pivot,
    Structural
}

public sealed record CriticalLevel(
    string Id,
    CriticalRole Role,
    string Method,
    Timeframe Timeframe,
    DateTimeOffset SourceCloseTime,
    decimal Price);

public sealed record CriticalZone(
    string Id,
    CriticalRole Role,
    string Method,
    Timeframe Timeframe,
    DateTimeOffset SourceCloseTime,
    decimal Low,
    decimal High);

public enum BreakDirection
{
    Up,
    Down
}

public sealed record Anchor(
    string StructureId,
    BreakDirection BreakDirection,
    DateTimeOffset CandleCloseTime,
    decimal High,
    decimal Low);

public sealed class CriticalStructureStore
{
    private readonly Dictionary<string, CriticalLevel> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CriticalZone> _zones = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Anchor> _anchors = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<CriticalLevel> Levels => _levels.Values;
    public IEnumerable<CriticalZone> Zones => _zones.Values;
    public IEnumerable<Anchor> Anchors => _anchors.Values;

    public void Add(CriticalLevel level) => _levels[level.Id] = level;
    public void Add(CriticalZone zone) => _zones[zone.Id] = zone;
    public void SetAnchor(Anchor anchor) => _anchors[anchor.StructureId] = anchor;

    public bool TryGetLevel(string id, out CriticalLevel level)
        => _levels.TryGetValue(id, out level!);

    public bool TryGetZone(string id, out CriticalZone zone)
        => _zones.TryGetValue(id, out zone!);

    public bool TryGetAnchor(string structureId, out Anchor anchor)
        => _anchors.TryGetValue(structureId, out anchor!);
    /// <summary>
    /// Removes the anchor associated with a structure. Called by helpers that
    /// replace a level (e.g. weekly pivot rotation) so the old anchor does not
    /// linger against a level that no longer exists.
    /// </summary>
    public bool ClearAnchor(string structureId) => _anchors.Remove(structureId);
}
