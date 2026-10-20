using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class LoggerTests
{
    [Fact]
    public async Task Writes_To_File_With_Ist_Timestamp()
    {
        var dir = TempFolder.Create();
        try
        {
            var section = new LoggingSection { Level = "Debug", RetainDays = 1, MaxFileSizeMB = 20 };
            await using (var log = CliLogger.Create(section, dir, consoleEnabled: false))
            {
                log.Information("hello-world");
                // Give the drain loop time to flush.
                await Task.Delay(400);
            }

            var files = Directory.GetFiles(dir, "backtest-*.log");
            Assert.Single(files);
            var text = File.ReadAllText(files[0]);
            Assert.Contains("hello-world", text);
            Assert.Contains(" IST]", text);
            Assert.Contains("[INF]", text);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Redacts_Secrets()
    {
        var dir = TempFolder.Create();
        try
        {
            var section = new LoggingSection { Level = "Debug", RetainDays = 1, MaxFileSizeMB = 20 };
            await using (var log = CliLogger.Create(section, dir, consoleEnabled: false))
            {
                log.Information("Connecting Server=.;Password=hunter2;Database=db;");
                log.Warning("Token=abc123XYZ");
                await Task.Delay(400);
            }

            var file = Directory.GetFiles(dir, "backtest-*.log")[0];
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("hunter2", text);
            Assert.DoesNotContain("abc123XYZ", text);
            Assert.Contains("Password=***", text);
            Assert.Contains("Token=***", text);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Level_Filter_Works()
    {
        var dir = TempFolder.Create();
        try
        {
            var section = new LoggingSection { Level = "Warning", RetainDays = 1, MaxFileSizeMB = 20 };
            await using (var log = CliLogger.Create(section, dir, consoleEnabled: false))
            {
                log.Debug("dbg-msg");
                log.Information("inf-msg");
                log.Warning("wrn-msg");
                log.Error("err-msg");
                await Task.Delay(400);
            }

            var file = Directory.GetFiles(dir, "backtest-*.log")[0];
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("dbg-msg", text);
            Assert.DoesNotContain("inf-msg", text);
            Assert.Contains("wrn-msg", text);
            Assert.Contains("err-msg", text);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Exception_Is_Logged_With_Type_And_Message()
    {
        var dir = TempFolder.Create();
        try
        {
            var section = new LoggingSection { Level = "Debug", RetainDays = 1, MaxFileSizeMB = 20 };
            await using (var log = CliLogger.Create(section, dir, consoleEnabled: false))
            {
                log.Error("context", new InvalidOperationException("boom"));
                await Task.Delay(400);
            }
            var file = Directory.GetFiles(dir, "backtest-*.log")[0];
            var text = File.ReadAllText(file);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("boom", text);
        }
        finally { TempFolder.Delete(dir); }
    }
    [Fact]
    public async Task Does_Not_Redact_Numeric_Token_But_Redacts_Real_Api_Token()
    {
        var dir = TempFolder.Create();
        try
        {
            var section = new LoggingSection { Level = "Debug", RetainDays = 1, MaxFileSizeMB = 20 };
            await using (var log = CliLogger.Create(section, dir, consoleEnabled: false))
            {
                log.Information("Backtest complete: strategy=Smoke, token=2885, trades=0");
                log.Information("Kite auth Token=abcdef0123456789abcdef0123456789");
                await Task.Delay(400);
            }
            var file = Directory.GetFiles(dir, "backtest-*.log")[0];
            var text = File.ReadAllText(file);
            Assert.Contains("token=2885", text);                // numeric ID preserved
            Assert.DoesNotContain("abcdef0123456789abcdef0123456789", text);   // real token redacted
            Assert.Contains("Token=***", text);
        }
        finally { TempFolder.Delete(dir); }
    }
}