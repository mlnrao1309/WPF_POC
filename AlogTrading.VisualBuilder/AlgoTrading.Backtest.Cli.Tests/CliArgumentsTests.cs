using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class CliArgumentsTests
{
    private static string MakeRuleFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "rule-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"strategyName\":\"Test\"}");
        return path;
    }

    [Fact]
    public void Help_ShowsUsage()
    {
        var a = CliArguments.Parse(new[] { "--help" });
        Assert.True(a.ShowHelp);
        Assert.Null(a.Error);
    }

    [Fact]
    public void UnknownFlag_Errors()
    {
        var a = CliArguments.Parse(new[] { "--wat" });
        Assert.NotNull(a.Error);
        Assert.Contains("Unknown", a.Error);
    }

    [Fact]
    public void Valid_SingleRun()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "2885",
                "--from", "2020-01-01", "--to", "2024-12-31"
            });
            Assert.Null(a.Error);
            Assert.False(a.Daemon);
            Assert.Equal(2885u, a.Instrument);
            Assert.Equal(new DateOnly(2020, 1, 1), a.From);
            Assert.Equal(new DateOnly(2024, 12, 31), a.To);
            Assert.False(a.Sweep);
            Assert.False(a.WalkForward);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void Sweep_And_WalkForward_Conflict()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "1",
                "--from", "2020-01-01", "--to", "2020-12-31",
                "--sweep", "--walk-forward"
            });
            Assert.NotNull(a.Error);
            Assert.Contains("--sweep", a.Error);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void Quiet_And_Verbose_Conflict()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "1",
                "--from", "2020-01-01", "--to", "2020-12-31",
                "--quiet", "--verbose"
            });
            Assert.NotNull(a.Error);
            Assert.Contains("--quiet", a.Error);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void From_GreaterThan_To_Errors()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "1",
                "--from", "2024-01-01", "--to", "2020-01-01"
            });
            Assert.NotNull(a.Error);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void Instrument_Zero_Errors()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "0",
                "--from", "2020-01-01", "--to", "2020-12-31"
            });
            Assert.NotNull(a.Error);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void InvalidDate_Errors()
    {
        var rule = MakeRuleFile();
        try
        {
            var a = CliArguments.Parse(new[]
            {
                "--rule", rule, "--instrument", "1",
                "--from", "01/01/2020", "--to", "2020-12-31"
            });
            Assert.NotNull(a.Error);
        }
        finally { File.Delete(rule); }
    }

    [Fact]
    public void Daemon_IgnoresOtherFlags()
    {
        var a = CliArguments.Parse(new[]
        {
            "--daemon", "--rule", "x.json", "--verbose", "--instrument", "1"
        });
        Assert.True(a.Daemon);
        Assert.True(a.HadIgnoredFlagsInDaemonMode);
        Assert.Null(a.Error);
    }

    [Fact]
    public void Daemon_Only_Config()
    {
        var a = CliArguments.Parse(new[] { "--daemon", "--config", "myapp.json" });
        Assert.True(a.Daemon);
        Assert.False(a.HadIgnoredFlagsInDaemonMode);
        Assert.Equal("myapp.json", a.ConfigPath);
    }

    [Fact]
    public void MissingRule_Errors()
    {
        var a = CliArguments.Parse(new[]
        {
            "--instrument", "1", "--from", "2020-01-01", "--to", "2020-12-31"
        });
        Assert.NotNull(a.Error);
    }

    [Fact]
    public void Rule_File_Must_Exist()
    {
        var a = CliArguments.Parse(new[]
        {
            "--rule", "definitely-not-here.json", "--instrument", "1",
            "--from", "2020-01-01", "--to", "2020-12-31"
        });
        Assert.NotNull(a.Error);
    }

    [Fact]
    public void MissingFlagValue_Errors()
    {
        var a = CliArguments.Parse(new[] { "--rule" });
        Assert.NotNull(a.Error);
    }
}