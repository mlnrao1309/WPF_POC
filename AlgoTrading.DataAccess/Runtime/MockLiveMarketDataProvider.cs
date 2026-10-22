using System.Runtime.CompilerServices;

namespace AlgoTrading.DataAccess.Runtime;

public class MockLiveMarketDataProvider : ILiveMarketDataProvider
{
    public async IAsyncEnumerable<LiveTickDto> StreamLiveCandlesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(5000, cancellationToken);

            // Yield an empty token just to keep the loop alive
            yield return new LiveTickDto
            {
                InstrumentToken = 0,
                Timeframe = "60",
                IsCandleClosed = false
            };
        }
    }
}