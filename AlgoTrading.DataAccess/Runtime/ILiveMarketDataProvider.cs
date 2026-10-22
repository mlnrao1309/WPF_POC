using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.Runtime;

public class LiveTickDto
{
    public long InstrumentToken { get; set; }
    public string Timeframe { get; set; } = string.Empty; // e.g., "60"
    public Candle NewCandle { get; set; } 
    public bool IsCandleClosed { get; set; } // True if the 60m bar just finished
}

public interface ILiveMarketDataProvider
{
    // This method should yield data whenever a WebSocket tick arrives 
    // OR when a REST poll sees a new candle.
    IAsyncEnumerable<LiveTickDto> StreamLiveCandlesAsync(CancellationToken cancellationToken);
}