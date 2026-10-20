namespace AlgoTrading.Backtest.Cli.Tests;

internal static class TempFolder
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "t7-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}

internal static class TestLogger
{
    public static CliLogger Silent(string logFolder)
    {
        var section = new LoggingSection
        {
            Level = "Debug",
            RetainDays = 1,
            MaxFileSizeMB = 20
        };
        return CliLogger.Create(section, logFolder, consoleEnabled: false);
    }
}