using System.Collections.Generic;

namespace AlgoTrading.ViewModels;

public sealed class MarketWatchNameItem : SelectableItemViewModel
{
    public int WatchId { get; }
    public string Name { get; }

    /// <summary>Populated by the parent VM when this item is loaded.</summary>
    public IReadOnlyList<MarketWatchInstrumentItem> Instruments { get; }

    public MarketWatchNameItem(int watchId, string name,
                               IReadOnlyList<MarketWatchInstrumentItem>? instruments = null)
    {
        WatchId = watchId;
        Name = name;
        Instruments = instruments ?? new List<MarketWatchInstrumentItem>();
    }

    public override string ToString() => Name;   // used by the ComboBox if no template matches
}