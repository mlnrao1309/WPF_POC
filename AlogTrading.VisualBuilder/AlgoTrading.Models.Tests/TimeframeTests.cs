using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Models.Tests;

public class TimeframeTests
{
    [Theory]
    [InlineData("1m",  TimeframeUnit.m, 1)]
    [InlineData("5m",  TimeframeUnit.m, 5)]
    [InlineData("15m", TimeframeUnit.m, 15)]
    [InlineData("25m", TimeframeUnit.m, 25)]
    [InlineData("1H",  TimeframeUnit.H, 1)]
    [InlineData("2H",  TimeframeUnit.H, 2)]
    [InlineData("D",   TimeframeUnit.D, 1)]
    [InlineData("W",   TimeframeUnit.W, 1)]
    [InlineData("M",   TimeframeUnit.M, 1)]
    public void Parse_RoundTrips(string s, TimeframeUnit u, int c)
    {
        var tf = Timeframe.Parse(s);
        tf.Unit.Should().Be(u);
        tf.Count.Should().Be(c);
        tf.ToString().Should().Be(s);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0m")]
    [InlineData("-1m")]
    [InlineData("2D")]
    [InlineData("3W")]
    [InlineData("12")]
    [InlineData("1.5m")]
    [InlineData("XM")]
    public void Parse_Rejects(string s)
    {
        Action a = () => Timeframe.Parse(s);
        a.Should().Throw<Exception>();
    }

    [Fact]
    public void ToMinutes_MinutesAndHours()
    {
        Timeframe.Parse("15m").ToMinutes().Should().Be(15);
        Timeframe.Parse("2H").ToMinutes().Should().Be(120);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("W")]
    [InlineData("M")]
    public void ToMinutes_ThrowsForPositional(string s)
    {
        Action a = () => Timeframe.Parse(s).ToMinutes();
        a.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_Works()
    {
        (Timeframe.Parse("15m") == Timeframe.Parse("15m")).Should().BeTrue();
        (Timeframe.Parse("15m") != Timeframe.Parse("1H")).Should().BeTrue();
    }

    [Fact]
    public void Compare_IntradayBeforePositional()
    {
        Timeframe.Parse("1H").CompareTo(Timeframe.Parse("D")).Should().BeNegative();
        Timeframe.Parse("5m").CompareTo(Timeframe.Parse("15m")).Should().BeNegative();
    }
}