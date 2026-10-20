using AlgoTrading.Data;
using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Data.Tests;

[Collection("Sql")]
public sealed class SqlBarSeriesTests
{
    [Fact]
    public void Load_ReturnsEmptyWhenNoRowsForRange()
    {
        string? cs = TestSql.ConnectionString;
        if (cs is null) return;

        var series = SqlBarSeries.Load(
            cs, 999_999, Timeframe.Parse("1m"),
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        series.Count.Should().Be(0);
    }
}