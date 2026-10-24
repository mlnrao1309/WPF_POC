using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Backtest.Tests;

public sealed class CalendarIndependentCandleTests
{
    [Fact]
    public void IndicatorConversion_DoesNotCollapseDistinctCandlesOnto1530()
    {
        // Supplied sorted timestamps are trusted, even when their spacing is irregular.
        var start=new DateTime(2024,11,3,9,30,0,DateTimeKind.Utc); // 15:00 IST, Sunday
        var source=EngineTestSeries.FromOhlc(Timeframe.Parse("1H"),start,
            (100,101,99,100),(102,103,101,102),(104,105,103,104));
        var bars=new EngineTestSeries
        {
            Timeframe=source.Timeframe,Open=source.Open,High=source.High,Low=source.Low,Close=source.Close,
            Volume=source.Volume,OI=source.OI,DateTime=new[]{start,start.AddMinutes(10),start.AddMinutes(20)}
        };
        var accessor=new TimeframeAccessor(1,bars,new TimeframeCache(bars));
        var result=new IndicatorProvider(accessor).Resolve(new IndicatorExpression
        {
            Indicator=IndicatorKind.Sma,Params=new(){{"period",2}}
        },bars.Timeframe);
        Assert.Equal(101m,result.Outputs[0][1]);
        Assert.Equal(103m,result.Outputs[0][2]);
        Assert.Equal(start.AddMinutes(80),TimeframeAccessor.BarEnd(bars,2));
    }

    [Theory]
    [InlineData("D","2024-11-03 23:45","2024-11-04 00:00")]
    [InlineData("W","2024-11-03 18:00","2024-11-04 00:00")]
    [InlineData("M","2024-11-30 18:00","2024-12-01 00:00")]
    public void PeriodClose_UsesDatetimeBoundaryWithoutTradingCalendar(string tf,string localOpen,string localEnd)
    {
        var open=DateTime.ParseExact(localOpen,"yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture).AddMinutes(-330);
        var end=DateTime.ParseExact(localEnd,"yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture).AddMinutes(-330);
        var bars=EngineTestSeries.FromOhlc(Timeframe.Parse(tf),open,(100,101,99,100));
        Assert.Equal(end,TimeframeAccessor.BarEnd(bars,0));
    }
}
