using Xunit;

namespace AlgoTrading.Aggregation.Tests;

public class SessionCalendarTests
{
    [Fact]
    public void ToIst_ConvertsUtcToIst()
    {
        var utc = new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc);
        var ist = SessionCalendar.ToIst(utc);
        Assert.Equal(new DateTime(2024, 1, 1, 9, 15, 0), ist);
    }

    [Fact]
    public void ToUtc_ConvertsIstToUtc()
    {
        var ist = new DateTime(2024, 1, 1, 9, 15, 0);
        var utc = SessionCalendar.ToUtc(ist);
        Assert.Equal(new DateTime(2024, 1, 1, 3, 45, 0), utc);
    }

    [Fact]
    public void MinutesSinceSessionStartIst_At0915_ReturnsZero()
    {
        var ist = new DateTime(2024, 1, 1, 9, 15, 0);
        Assert.Equal(0, SessionCalendar.MinutesSinceSessionStartIst(ist));
    }

    [Fact]
    public void MinutesSinceSessionStartIst_At1530_Returns375()
    {
        var ist = new DateTime(2024, 1, 1, 15, 30, 0);
        Assert.Equal(375, SessionCalendar.MinutesSinceSessionStartIst(ist));
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday, true)]
    [InlineData(DayOfWeek.Sunday, true)]
    [InlineData(DayOfWeek.Monday, false)]
    [InlineData(DayOfWeek.Friday, false)]
    public void IsWeekend_ReturnsExpected(DayOfWeek dow, bool expected)
    {
        Assert.Equal(expected, SessionCalendar.IsWeekend(dow));
    }

    [Fact]
    public void IsWithinSessionIst_BeforeOpen_ReturnsFalse()
    {
        var ist = new DateTime(2024, 1, 1, 9, 14, 0);
        Assert.False(SessionCalendar.IsWithinSessionIst(ist));
    }

    [Fact]
    public void IsWithinSessionIst_AtOpen_ReturnsTrue()
    {
        var ist = new DateTime(2024, 1, 1, 9, 15, 0);
        Assert.True(SessionCalendar.IsWithinSessionIst(ist));
    }

    [Fact]
    public void IsWithinSessionIst_AtClose_ReturnsFalse()
    {
        var ist = new DateTime(2024, 1, 1, 15, 30, 0);
        Assert.False(SessionCalendar.IsWithinSessionIst(ist));
    }

    [Fact]
    public void GetIsoWeek_Jan1_2024_IsWeek1()
    {
        var (year, week) = SessionCalendar.GetIsoWeek(new DateTime(2024, 1, 1));
        Assert.Equal(2024, year);
        Assert.Equal(1, week);
    }

    [Fact]
    public void GetIsoWeek_Dec31_2024_IsWeek1Of2025()
    {
        var (year, week) = SessionCalendar.GetIsoWeek(new DateTime(2024, 12, 31));
        Assert.Equal(2025, year);
        Assert.Equal(1, week);
    }
}
