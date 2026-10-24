using AlgoTrading.Data.Daos;
using AlgoTrading.Data;
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
    public void Preparation_LoadsNestedTimeframesAndAggregatesMissingCandles()
    {
        string? cs=Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if(string.IsNullOrWhiteSpace(cs))return;
        using var scope=new TransactionScope(TransactionScopeOption.RequiresNew);
        using var connection=new SqlConnection(cs);
        SeedPreparationSession(connection,5);
        SeedPreparationSession(connection,15,false);
        const string json="""{"baseTimeframe":"5m","evaluationTimeframe":"1H","entry":{"left":{"timeframe":"15m","source":{"left":{"timeframe":"25m"}}}}}""";
        var series=PrepareFixture(cs,json);
        Assert.Equal(4,series.Count);
        var hourly=(SqlBarSeries)series[Timeframe.Parse("1H")];
        Assert.Equal(7,hourly.Count);
        Assert.Equal(Timeframe.Parse("5m"),hourly.AggregatedFrom);
        Assert.Equal(new DateTime(2026,1,2,10,45,0,DateTimeKind.Utc),hourly.CloseTime.Span[^1]);
        var target=series[Timeframe.Parse("25m")];
        Assert.Equal(15,target.Count);
        Assert.Equal(100m,target.Open.Span[0]);Assert.Equal(105m,target.High.Span[0]);
        Assert.Equal(99m,target.Low.Span[0]);Assert.Equal(104.5m,target.Close.Span[0]);
        Assert.Equal(5m,target.Volume.Span[0]);Assert.Equal(4m,target.OI.Span[0]);
        Assert.Null(((SqlBarSeries)series[Timeframe.Parse("15m")]).AggregatedFrom);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("misaligned")]
    [InlineData("invalid")]
    public void Preparation_UsesStoredDataWithoutSessionGridValidation(string fault)
    {
        string? cs=Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if(string.IsNullOrWhiteSpace(cs))return;
        using var scope=new TransactionScope(TransactionScopeOption.RequiresNew);
        using var connection=new SqlConnection(cs);
        SeedPreparationSession(connection,5);
        SeedPreparationSession(connection,15,false);
        const string selector="InstrumentToken=265 AND RTRIM(TimeFrame)='15' AND TimeStamp='20260102 09:30:00'";
        string mutation=fault switch
        {
            "missing"=>"DELETE dbo.Instruments_OHLC WHERE "+selector,
            "duplicate"=>"INSERT dbo.Instruments_OHLC (InstrumentToken,TimeFrame,TimeStamp,[Open],High,Low,[Close],Volume,OI) SELECT InstrumentToken,TimeFrame,TimeStamp,[Open],High,Low,[Close],Volume,OI FROM dbo.Instruments_OHLC WHERE "+selector,
            "misaligned"=>"UPDATE dbo.Instruments_OHLC SET TimeStamp=DATEADD(minute,1,TimeStamp) WHERE "+selector,
            _=>"UPDATE dbo.Instruments_OHLC SET High=Low-1 WHERE "+selector
        };
        if(fault=="duplicate")
        {
            Assert.Equal(2627,Assert.Throws<SqlException>(()=>connection.Execute(mutation)).Number);
            return; // The candle table itself forbids duplicate identities.
        }
        connection.Execute(mutation);
        var prepared=PrepareFixture(cs,"""{"baseTimeframe":"5m","evaluationTimeframe":"15m"}""");
        var stored=prepared[Timeframe.Parse("15m")];
        Assert.Equal(fault=="missing" ? 24 : 25,stored.Count);
        Assert.Null(((SqlBarSeries)stored).AggregatedFrom);
        if(fault=="misaligned") Assert.Contains(new DateTime(2026,1,2,4,1,0,DateTimeKind.Utc),stored.DateTime.ToArray());
        if(fault=="invalid") Assert.True(stored.High.Span[1]<stored.Low.Span[1]);
    }

    [Fact]
    public void Preparation_RejectsIncompatibleMissingTimeframe()
    {
        string? cs=Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if(string.IsNullOrWhiteSpace(cs))return;
        using var scope=new TransactionScope(TransactionScopeOption.RequiresNew);
        using var connection=new SqlConnection(cs);
        SeedPreparationSession(connection,15);
        var error=Assert.Throws<SqlException>(()=>PrepareFixture(cs,"""{"baseTimeframe":"15m","evaluationTimeframe":"25m"}"""));
        Assert.Contains("Cannot supply or aggregate required 25m",error.Message);
    }

    [Theory]
    [InlineData("W","2026-01-05","2026-01-18")]
    [InlineData("M","2026-01-01","2026-02-28")]
    public void Preparation_BuildsCompleteCalendarPeriods(string tf,string start,string finish)
    {
        string? cs=Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if(string.IsNullOrWhiteSpace(cs) && Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_CONFIG") is { } configPath)
        {
            using var config=JsonDocument.Parse(File.ReadAllText(configPath));
            cs=config.RootElement.GetProperty("Data").GetProperty("Sql").GetProperty("ConnectionString").GetString();
        }
        if(string.IsNullOrWhiteSpace(cs))return;
        using var scope=new TransactionScope(TransactionScopeOption.RequiresNew);
        using var connection=new SqlConnection(cs);
        var from=DateTime.Parse(start);var to=DateTime.Parse(finish);
        connection.Execute("DELETE dbo.Instruments_OHLC WHERE InstrumentToken=265 AND TimeStamp>=@from AND TimeStamp<DATEADD(day,1,@to)",new{from,to});
        connection.Execute("DELETE dbo.Instruments_CalculatedPivots WHERE InstrumentToken=265 AND TimeStamp>=@from AND TimeStamp<DATEADD(day,1,@to)",new{from,to});
        for(var date=from;date<=to;date=date.AddDays(1))
            connection.Execute("INSERT dbo.Instruments_OHLC (InstrumentToken,TimeFrame,TimeStamp,[Open],High,Low,[Close],Volume,OI) VALUES(265,'D',@date,100,105,95,102,1,0)",new{date});
        var prepared=SqlBarSeries.Prepare(cs,265,JsonSerializer.Serialize(new{baseTimeframe="D",evaluationTimeframe=tf}),
            DateTime.SpecifyKind(from.AddMinutes(-330),DateTimeKind.Utc),DateTime.SpecifyKind(to.AddDays(1).AddTicks(-1).AddMinutes(-330),DateTimeKind.Utc));
        Assert.Equal(2,prepared[Timeframe.Parse(tf)].Count);
        Assert.Equal(prepared[Timeframe.Parse("D")].Volume.ToArray().Sum(),prepared[Timeframe.Parse(tf)].Volume.ToArray().Sum());
    }

    private static IReadOnlyDictionary<Timeframe,IBarSeries> PrepareFixture(string cs,string json) => SqlBarSeries.Prepare(cs,265,json,
        new DateTime(2026,1,1,18,30,0,DateTimeKind.Utc),new DateTime(2026,1,2,18,29,59,DateTimeKind.Utc));

    private static void SeedPreparationSession(SqlConnection connection,int minutes,bool clear=true)
    {
        if(clear)connection.Execute("DELETE dbo.Instruments_OHLC WHERE InstrumentToken=265 AND TimeStamp>='20260102' AND TimeStamp<'20260103'");
        for(int i=0;i*minutes<375;i++)
            connection.Execute("INSERT dbo.Instruments_OHLC (InstrumentToken,TimeFrame,TimeStamp,[Open],High,Low,[Close],Volume,OI) VALUES(265,@tf,@at,@price,@high,@low,@close,1,@i)",
                new{tf=minutes.ToString(),at=new DateTime(2026,1,2,9,15,0).AddMinutes(i*minutes),price=100m+i,high=101m+i,low=99m+i,close=100.5m+i,i});
    }

    [Fact]
    public void CfCoreHistory_PersistsRiskAssessment_WithoutChangingManualApproval()
    {
        string? cs=Environment.GetEnvironmentVariable("ALGOTRADING_HISTORY_TEST_SQL");
        if(string.IsNullOrWhiteSpace(cs))return;
        using var scope=new TransactionScope(TransactionScopeOption.RequiresNew);
        var dao=new BacktestRunsDao(cs);
        using var conn=new SqlConnection(cs);
        string name="Risk assessment "+Guid.NewGuid().ToString("N");
        var from=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
        foreach(bool? approval in new bool?[]{false,true,null})
        {
            var id=Guid.NewGuid();
            dao.StartRun(id,name,"risk-test",265,"Sql","NextBarOpen/Delivery",from,from.AddDays(1),
                """{"runMode":"Single","lossBasis":"IncludeUnrealized","rule":{"direction":"Long"}}""",50000);
            conn.Execute("UPDATE dbo.StrategyMain SET IsBacktest=0,IsApproved=1 WHERE StrategyName=@name",new{name});
            var risk=new {isBacktestApproved=approval,lossBasis="includeUnrealized",dailyLossLimit=1000,monthlyLossLimit=2500,
                breaches=approval==false ? new[]{new{period="daily",loss=1000,limit=1000}} : Array.Empty<object>()};
            string json=JsonSerializer.Serialize(new{reproducibility=new{runId=id},metrics=new{totalNetPnL=0},riskAssessment=risk});
            dao.CompleteRun(id,json,"risk-test.json");
            var row=conn.QuerySingle("SELECT r.IsBacktestApproved,r.RiskAssessmentJson,s.IsApproved FROM dbo.StrategyBacktestResult r JOIN dbo.StrategyMain s ON s.StrategyId=r.StrategyId WHERE r.RunId=@id",new{id});
            Assert.Equal(approval,(bool?)row.IsBacktestApproved);
            Assert.True((bool)row.IsApproved);
            Assert.Equal(JsonSerializer.Serialize(risk),(string)row.RiskAssessmentJson);
            var recent=conn.Query("dbo.StrategyBacktest_Recent",new{Limit=500},commandType:CommandType.StoredProcedure).Single(r=>(Guid)r.RunId==id);
            Assert.Equal(approval,(bool?)recent.IsBacktestApproved);
            Assert.Equal("IncludeUnrealized",(string)recent.LossBasis);
        }
    }

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
