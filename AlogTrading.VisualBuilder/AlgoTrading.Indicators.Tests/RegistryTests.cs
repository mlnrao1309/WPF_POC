using System.Linq;
using Xunit;
using AlgoTrading.Indicators;

namespace AlgoTrading.Indicators.Tests;

public class RegistryTests
{
    [Fact]
    public void ExactlyTwentySeven()
    {
        Assert.Equal(27, IndicatorRegistry.All.Count);
    }

    [Fact]
    public void EveryDescriptorHasOutputs()
    {
        foreach (var d in IndicatorRegistry.All) Assert.NotEmpty(d.Outputs);
    }

    [Fact]
    public void EveryKindRoundTrips()
    {
        foreach (var d in IndicatorRegistry.All)
        {
            Assert.True(IndicatorRegistry.TryGet(d.Kind, out var got));
            Assert.Equal(d.Kind, got.Kind);
        }
    }

    [Fact]
    public void OutputsUniquePerDescriptor()
    {
        foreach (var d in IndicatorRegistry.All)
            Assert.Equal(d.Outputs.Count, d.Outputs.Distinct().Count());
    }
}
