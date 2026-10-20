using AlgoTrading.Backtest.Cli.Bridge;
using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class ResultWriterTests
{
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