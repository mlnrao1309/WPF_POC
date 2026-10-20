using AlgoTrading.Data.Daos;
using AlgoTrading.Models;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Data.Tests;

[Collection("Sql")]
public sealed class BacktestRunsDaoTests
{
    [Fact]
    public async Task InsertRunning_Then_RecoverStaleRunning_MarksFailed()
    {
        string? cs = TestSql.ConnectionString;
        if (cs is null) return;

        var dao = new BacktestRunsDao(cs);
        Guid runId = Guid.NewGuid();
        int strategyId = await TestSql.EnsureStrategyAsync(cs, "T3Test_Stale");
        int instToken = await TestSql.EnsureInstrumentAsync(cs, 2885, "RELIANCE");

        await dao.InsertRunningAsync(
            runId, strategyId, null, (uint)instToken, Timeframe.Parse("5m"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            "Single", "1.0.0", "Sql", "tester");

        var row = await dao.GetByIdAsync(runId);
        row.Should().NotBeNull();
        row!.Status.Should().Be("Running");

        int recovered = await dao.RecoverStaleRunningAsync();
        recovered.Should().BeGreaterThanOrEqualTo(1);

        var after = await dao.GetByIdAsync(runId);
        after!.Status.Should().Be("Failed");
        after.ErrorMessage.Should().Contain("Recovered");
    }

    [Fact]
    public async Task UpdateCompleted_SetsMetricsAndStatus()
    {
        string? cs = TestSql.ConnectionString;
        if (cs is null) return;

        var dao = new BacktestRunsDao(cs);
        Guid runId = Guid.NewGuid();
        int strategyId = await TestSql.EnsureStrategyAsync(cs, "T3Test_Complete");
        int instToken = await TestSql.EnsureInstrumentAsync(cs, 2885, "RELIANCE");

        await dao.InsertRunningAsync(
            runId, strategyId, null, (uint)instToken, Timeframe.Parse("5m"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            "Single", "1.0.0", "Sql", "tester");

        await dao.UpdateCompletedAsync(runId, "{\"netPnl\":42}", @"C:\results\run_result.json");

        var row = await dao.GetByIdAsync(runId);
        row!.Status.Should().Be("Completed");
        row.MetricsJson.Should().Be("{\"netPnl\":42}");
        row.ResultFilePath.Should().EndWith("run_result.json");
    }

    [Fact]
    public async Task UpdateFailed_TruncatesLongErrorMessage()
    {
        string? cs = TestSql.ConnectionString;
        if (cs is null) return;

        var dao = new BacktestRunsDao(cs);
        Guid runId = Guid.NewGuid();
        int strategyId = await TestSql.EnsureStrategyAsync(cs, "T3Test_Fail");
        int instToken = await TestSql.EnsureInstrumentAsync(cs, 2885, "RELIANCE");

        await dao.InsertRunningAsync(
            runId, strategyId, null, (uint)instToken, Timeframe.Parse("5m"),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            "Single", "1.0.0", "Sql", "tester");

        string huge = new string('x', 5000);
        await dao.UpdateFailedAsync(runId, huge);

        var row = await dao.GetByIdAsync(runId);
        row!.Status.Should().Be("Failed");
        row.ErrorMessage!.Length.Should().Be(2000);
    }
}