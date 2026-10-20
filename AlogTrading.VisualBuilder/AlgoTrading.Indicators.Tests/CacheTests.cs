using System.Threading.Tasks;
using Xunit;
using AlgoTrading.Models;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class CacheTests
{
    [Fact]
    public void SameKeyReturnsSameInstance()
    {
        var cache = new IndicatorCache();
        var tf = Timeframe.Parse("15m");
        var key = new IndicatorCacheKey(1, tf, IndicatorKind.Sma, "Value", "period=20", "field:default@0:Close");
        var a = cache.GetOrAdd(key, _ => new decimal?[10]);
        var b = cache.GetOrAdd(key, _ => new decimal?[10]);
        Assert.Same(a, b);
    }

    [Fact]
    public async Task ThreadSafe()
    {
        var cache = new IndicatorCache();
        var tf = Timeframe.Parse("15m");
        var key = new IndicatorCacheKey(1, tf, IndicatorKind.Ema, "Value", "period=20", "s");
        var tasks = new[]
        {
            Task.Run(() => cache.GetOrAdd(key, _ => new decimal?[10])),
            Task.Run(() => cache.GetOrAdd(key, _ => new decimal?[10])),
            Task.Run(() => cache.GetOrAdd(key, _ => new decimal?[10]))
        };
        var results = await Task.WhenAll(tasks);
        Assert.Same(results[0], results[1]);
        Assert.Same(results[1], results[2]);
    }
}
