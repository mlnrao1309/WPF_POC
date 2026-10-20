using AlgoTrading.Data;
using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Data.Tests;

public sealed class InstrumentLookupTests
{
    [Fact]
    public void InMemory_FindsKnownToken()
    {
        var lookup = new InMemoryInstrumentLookup(new[]
        {
            new InstrumentInfo { Token = 2885, Symbol = "RELIANCE", DisplayName = "Reliance Industries", InstrumentType = InstrumentType.Equity, LotSize = 1, TickSize = 0.05m },
            new InstrumentInfo { Token = 11536, Symbol = "TCS", DisplayName = "Tata Consultancy", InstrumentType = InstrumentType.Equity, LotSize = 1, TickSize = 0.05m }
        });

        lookup.TryGet(2885, out var info).Should().BeTrue();
        info.Symbol.Should().Be("RELIANCE");
        info.InstrumentType.Should().Be(InstrumentType.Equity);
    }

    [Fact]
    public void InMemory_ReturnsFalseForUnknownToken()
    {
        var lookup = new InMemoryInstrumentLookup(Array.Empty<InstrumentInfo>());
        lookup.TryGet(42, out _).Should().BeFalse();
    }
}