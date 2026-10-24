using AlgoTrading.Data.Daos;
using AlgoTrading.Models;
using FluentAssertions;
using Xunit;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Transactions;

namespace AlgoTrading.Data.Tests;

[Collection("Sql")]
public sealed class BacktestRunsDaoTests
{
    [Fact]
    public void CfCoreHistory_PersistsLifecycleAndRejectsMismatchedResults()
    {
        string? cs = Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if (string.IsNullOrWhiteSpace(cs)) return;

        // Roll back every test row, including the registered strategy.
        using var scope = new TransactionScope(TransactionScopeOption.RequiresNew);
        var dao = new BacktestRunsDao(cs);
        string name = "History test " + Guid.NewGuid().ToString("N");
        Guid completed = Guid.NewGuid(), failed = Guid.NewGuid(), cancelled = Guid.NewGuid();
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const string config = """{"runMode":"Single","rule":{"direction":"Short"}}""";
        dao.StartRun(completed, name, "v1", 265, "Sql", "NextBarOpen/Delivery",
            from, from.AddDays(1), config, 100000m);
        using (var conn = new SqlConnection(cs))
        {
            var running = conn.QuerySingle("SELECT RunStatus, FinishedAt FROM dbo.StrategyBacktestResult WHERE RunId=@completed", new { completed });
            Assert.Equal("Running", (string)running.RunStatus);
            Assert.Null(running.FinishedAt);
        }

        string result = JsonSerializer.Serialize(new
        {
            reproducibility = new { runId = completed },
            metrics = new { totalNetPnL = 42m, netProfitPct = .042m, totalTrades = 1,
                winningTrades = 1, losingTrades = 0, breakevenTrades = 0,
                winRatePct = 100m, maxDrawdownAbsolute = 0m, maxDrawdownPct = 0m },
            trades = new[] { new { netPnL = 42m } }, equityCurve = new[] { new { equity = 100042m } }
        });
        dao.CompleteRun(completed, result, "test.json");
        dao.FailRun(completed, "Late failure must not overwrite completion.");
        dao.StartRun(failed, name, "v2", 265, "Sql", "NextBarOpen/Delivery",
            from, from.AddDays(1), config, 100000m);
        dao.FailRun(failed, new string('x', 3000));
        dao.StartRun(cancelled, name, "v3", 265, "Sql", "NextBarOpen/Delivery",
            from, from.AddDays(1), config, 100000m);
        dao.FailRun(cancelled, "Cancelled by test.", cancelled: true);

        using var verify = new SqlConnection(cs);
        var rows = verify.Query("dbo.StrategyBacktest_Recent", new { Limit = 500 },
            commandType: CommandType.StoredProcedure).Where(r => (string)r.StrategyName == name).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => (Guid)r.RunId == completed && (string)r.Status == "Completed" && (decimal)r.NetPnL == 42m && (string)r.Direction == "Short");
        Assert.Contains(rows, r => (Guid)r.RunId == failed && (string)r.Status == "Failed" && ((string)r.ErrorMessage).Length == 2000);
        Assert.Contains(rows, r => (Guid)r.RunId == cancelled && (string)r.Status == "Cancelled");
        Assert.Single(rows.Select(r => (int)r.StrategyId).Distinct());
        Assert.Equal(config, verify.QuerySingle<string>("SELECT ConfigurationJson FROM dbo.StrategyBacktestResult WHERE RunId=@completed", new { completed }));
        verify.Close();
        Assert.Throws<SqlException>(() => dao.CompleteRun(completed,
            result.Replace(completed.ToString(), Guid.NewGuid().ToString()), "test.json"));
    }

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
