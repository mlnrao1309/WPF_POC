using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Load_Full_Config()
    {
        var dir = TempFolder.Create();
        try
        {
            var cfg = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(cfg, @"{
  ""Data"": { ""Source"": ""Csv"", ""Csv"": { ""FolderPath"": ""data"" } },
  ""Paths"": { ""RulesPending"": ""rules/pending"", ""Results"": ""results"", ""Logs"": ""logs"" },
  ""Configs"": { ""CostsPath"": ""configs/costs.json"" },
  ""Backtest"": { ""InitialCapital"": 250000, ""RiskFreeRatePercent"": 3.0,
                 ""MaxSweepCombinations"": 5000, ""WalkForward"": { ""InSampleRatio"": 0.6, ""WindowCount"": 4 } },
  ""Logging"": { ""Level"": ""Debug"", ""RetainDays"": 7, ""MaxFileSizeMB"": 10 }
}");
            var s = AppSettingsLoader.Load(cfg);
            Assert.Equal("Csv", s.Data.Source);
            Assert.Equal(250000m, s.Backtest.InitialCapital);
            Assert.Equal(3.0m, s.Backtest.RiskFreeRatePercent);
            Assert.Equal(5000, s.Backtest.MaxSweepCombinations);
            Assert.Equal(0.6, s.Backtest.WalkForward.InSampleRatio);
            Assert.Equal(4, s.Backtest.WalkForward.WindowCount);
            Assert.Equal("Debug", s.Logging.Level);
            Assert.Equal(7, s.Logging.RetainDays);
            Assert.Equal(Path.GetFullPath(dir), s.BaseDirectory);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Load_Default_When_Sections_Missing()
    {
        var dir = TempFolder.Create();
        try
        {
            var cfg = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(cfg, "{}");
            var s = AppSettingsLoader.Load(cfg);
            Assert.Equal("Sql", s.Data.Source);
            Assert.Equal(100_000m, s.Backtest.InitialCapital);
            Assert.Equal("rules/pending", s.Paths.RulesPending);
            Assert.Equal("Information", s.Logging.Level);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void ResolvePath_Relative_Against_BaseDirectory()
    {
        var dir = TempFolder.Create();
        try
        {
            var cfg = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(cfg, @"{
  ""Paths"": { ""Logs"": ""logs"" }
}");
            var s = AppSettingsLoader.Load(cfg);
            var p = AppSettingsLoader.ResolvePath(s, s.Paths.Logs);
            Assert.Equal(Path.Combine(Path.GetFullPath(dir), "logs"), p);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void ResolvePath_Absolute_Passthrough()
    {
        var dir = TempFolder.Create();
        try
        {
            var cfg = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(cfg, "{}");
            var s = AppSettingsLoader.Load(cfg);

            var abs = Path.Combine(Path.GetTempPath(), "absolute-logs");
            var p = AppSettingsLoader.ResolvePath(s, abs);
            Assert.Equal(Path.GetFullPath(abs), p);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void ResolveConfigPath_Explicit_Missing_ReturnsNull()
    {
        var p = AppSettingsLoader.ResolveConfigPath("no-such-file-xyz.json");
        Assert.Null(p);
    }
}