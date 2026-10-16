using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;

namespace AlgoTrading.ViewModels;

public sealed class MarketWatchInstrumentItem : SelectableItemViewModel
{
    public MarketWatch Source { get; }

    public MarketWatchInstrumentItem(MarketWatch source)
    {
        Source = source;
    }

    public int WatchId => Source.WatchId;
    public string Name => Source.Name;
    public long InstrumentToken => Source.InstrumentToken;
    public string? TradingSymbol => Source.TradingSymbol;

    /// <summary>Maps to the Segment column, which is what the grid shows as "InstrumentType".</summary>
    public string? InstrumentType => Source.Segment;

    public string? Exchange => Source.Exchange;

    public bool IsActive
    {
        get => Source.IsActive;
        set
        {
            if (Source.IsActive == value) return;
            Source.IsActive = value;
            OnPropertyChanged();
        }
    }
}