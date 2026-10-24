using AlgoTrading.Backtest.Cli.Bridge;
using Xunit;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Transactions;

namespace AlgoTrading.Backtest.Cli.Tests;

public class ResultWriterTests
{
    [Fact]
    public async Task CfCoreFacadeHistory_RecordsRealBacktestAndValidationFailure()
    {
        string? configPath = Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_CONFIG");
        if (string.IsNullOrWhiteSpace(configPath)) return;
        var settings = AppSettingsLoader.Load(configPath);
        var dir = TempFolder.Create();
        try
        {
            using var transaction = new TransactionScope(TransactionScopeOption.RequiresNew,
                TransactionScopeAsyncFlowOption.Enabled);
            await using var logger = TestLogger.Silent(Path.Combine(dir, "logs"));
            var facade = new BacktestFacade(settings);
            var processor = new JobProcessor(logger, facade, new ResultWriter(Path.Combine(dir, "results")), 1);
            string name = "History integration " + Guid.NewGuid().ToString("N");
            var jobPath = Path.Combine(dir, "integration.job.json");
            var rulePath = Path.Combine(dir, "integration.json");
            File.WriteAllText(jobPath, """{"schemaVersion":1,"strategyFile":"integration.json","instrumentToken":265,"from":"2026-09-01","to":"2026-09-30","mode":"Single"}""");
            string rule = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, strategyName = name, direction = "Long",
                evaluationTimeframe = "15m", baseTimeframe = "15m", executionMode = "Delivery",
                parameters = Array.Empty<object>(),
                entry = new { enabled = true, logic = "All", children = new[] { new
                {
                    kind = "condition", enabled = true,
                    left = new { kind = "field", field = "Close", offset = 0 },
                    @operator = "GreaterThan", right = new { kind = "number", value = 0 }
                } } },
                fullExit = new { enabled = true, logic = "All", children = Array.Empty<object>() }
            });
            File.WriteAllText(rulePath, rule);
            var outcome = processor.Process(jobPath, "integration.json");
            Assert.True(outcome.Success, outcome.ErrorMessage);
            var resultPath = Path.Combine(dir, "results", outcome.ResultBaseName + "_result.json");
            using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
            var id = result.RootElement.GetProperty("reproducibility").GetProperty("runId").GetGuid();
            using (var conn = new SqlConnection(settings.Data.Sql.ConnectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "dbo.StrategyBacktest_Recent";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Limit", 500);
                using var reader = cmd.ExecuteReader();
                bool found = false;
                while (reader.Read())
                {
                    if (reader.GetGuid(reader.GetOrdinal("RunId")) != id) continue;
                    found = true;
                    Assert.Equal("Completed", reader["Status"]);
                    Assert.Equal(resultPath, reader["ResultFilePath"]);
                    Assert.Equal(result.RootElement.GetProperty("metrics").GetProperty("totalTrades").GetInt64(), (long)reader["TotalTrades"]);
                }
                Assert.True(found);
            }
            // Valid JSON with an invalid rule must create a visible Failed history row.
            File.WriteAllText(rulePath, JsonSerializer.Serialize(new { strategyName = name, executionMode = "Delivery" }));
            var failure = processor.Process(jobPath, "integration.json");
            Assert.False(failure.Success);
            using var verify = new SqlConnection(settings.Data.Sql.ConnectionString);
            verify.Open();
            using var check = verify.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM dbo.StrategyBacktestResult r JOIN dbo.StrategyMain s ON s.StrategyId=r.StrategyId WHERE s.StrategyName=@name AND r.RunStatus='Failed' AND r.FinishedAt IS NOT NULL";
            check.Parameters.AddWithValue("@name", name);
            Assert.Equal(1, (int)check.ExecuteScalar()!);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JobProcessor_RecordsHistoryAfterWriting_AndReportsHistoryFailure(bool failCompletion)
    {
        var dir = TempFolder.Create();
        try
        {
            var job = Path.Combine(dir, "strategy.job.json");
            File.WriteAllText(job, """{"schemaVersion":1,"strategyFile":"strategy.json","instrumentToken":265,"from":"2025-01-01","to":"2025-01-02","mode":"Single"}""");
            File.WriteAllText(Path.Combine(dir, "strategy.json"), """{"strategyName":"History test"}""");
            await using var logger = TestLogger.Silent(Path.Combine(dir, "logs"));
            var facade = new HistoryRecordingFacade { FailCompletion = failCompletion };
            var processor = new JobProcessor(logger, facade, new ResultWriter(Path.Combine(dir, "results")), 1);
            var outcome = processor.Process(job, "strategy.json");

            Assert.NotNull(facade.Request);
            Assert.NotEqual(Guid.Empty, facade.Request.RunId);
            Assert.Equal(facade.Request.RunId, facade.CompletedRunId);
            Assert.True(facade.FilesExistAtCompletion);
            Assert.Equal(!failCompletion, outcome.Success);
            if (failCompletion)
            {
                Assert.Equal(facade.Request.RunId, facade.FailedRunId);
                Assert.Contains("History write failed", outcome.ErrorMessage);
            }
            else Assert.Null(facade.FailedRunId);
        }
        finally { TempFolder.Delete(dir); }
    }

    private sealed class HistoryRecordingFacade : IBacktestFacade
    {
        public bool FailCompletion { get; init; }
        public FacadeRequest? Request { get; private set; }
        public Guid? CompletedRunId { get; private set; }
        public Guid? FailedRunId { get; private set; }
        public bool FilesExistAtCompletion { get; private set; }

        public FacadeResult Run(FacadeRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return new StubBacktestFacade().Run(request, cancellationToken);
        }

        public void CompleteRun(Guid runId, string resultJson, string resultFilePath)
        {
            CompletedRunId = runId;
            FilesExistAtCompletion = File.Exists(resultFilePath)
                && File.Exists(resultFilePath.Replace("_result.json", "_trades.csv"))
                && File.Exists(resultFilePath.Replace("_result.json", "_summary.txt"));
            if (FailCompletion) throw new InvalidOperationException("History write failed");
        }

        public void FailRun(Guid runId, string errorMessage) => FailedRunId = runId;
    }

    [Fact]
    public void BuildBaseName_Uses_Utc_Format()
    {
        var bn = ResultWriter.BuildBaseName(
            "My Strategy!",
            2885u,
            new DateOnly(2020, 1, 1),
            new DateOnly(2024, 12, 31),
            new DateTime(2026, 1, 1, 12, 0, 2, DateTimeKind.Utc));
        Assert.Equal("My_Strategy__2885_2020-01-01_2024-12-31_20260101T120002Z", bn);
    }

    [Fact]
    public void BuildBaseName_Truncates_Long_Names()
    {
        var longName = new string('a', 200);
        var bn = ResultWriter.BuildBaseName(
            longName, 1u, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var parts = bn.Split('_');
        Assert.Equal(60, parts[0].Length);
    }

    [Fact]
    public void Write_Produces_Four_Files()
    {
        var dir = TempFolder.Create();
        try
        {
            var writer = new ResultWriter(dir);
            var outcome = writer.Write("test-base", FacadeResult.Ok("{}", "a,b\r\n", "summary", new RunMetricsSummary()));

            Assert.True(File.Exists(outcome.ResultPath));
            Assert.True(File.Exists(outcome.TradesPath));
            Assert.True(File.Exists(outcome.SummaryPath));
            Assert.True(File.Exists(outcome.NotifyPath));

            Assert.EndsWith("_result.json", outcome.ResultPath);
            Assert.EndsWith("_trades.csv", outcome.TradesPath);
            Assert.EndsWith("_summary.txt", outcome.SummaryPath);
            Assert.EndsWith(".notify", outcome.NotifyPath);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Notify_Contains_Path_Without_Trailing_Newline()
    {
        var dir = TempFolder.Create();
        try
        {
            var writer = new ResultWriter(dir);
            var outcome = writer.Write("t", FacadeResult.Ok("{}", "", "", new RunMetricsSummary()));
            var text = File.ReadAllText(outcome.NotifyPath);
            Assert.Equal(outcome.ResultPath, text);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Write_Is_Idempotent()
    {
        var dir = TempFolder.Create();
        try
        {
            var writer = new ResultWriter(dir);
            writer.Write("same", FacadeResult.Ok("{\"a\":1}", "", "", new RunMetricsSummary()));
            writer.Write("same", FacadeResult.Ok("{\"a\":2}", "", "", new RunMetricsSummary()));

            var content = File.ReadAllText(Path.Combine(dir, "same_result.json"));
            Assert.Contains("\"a\":2", content);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void No_Tmp_Files_Left_Behind()
    {
        var dir = TempFolder.Create();
        try
        {
            var writer = new ResultWriter(dir);
            writer.Write("z", FacadeResult.Ok("{}", "", "", new RunMetricsSummary()));
            var tmpFiles = Directory.GetFiles(dir, "*.tmp");
            Assert.Empty(tmpFiles);
        }
        finally { TempFolder.Delete(dir); }
    }
}
