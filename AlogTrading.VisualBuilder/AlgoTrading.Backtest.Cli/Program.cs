using AlgoTrading.Backtest.Cli;
using AlgoTrading.Backtest.Cli.Bridge;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliArguments parsed;
        try { parsed = CliArguments.Parse(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (parsed.ShowHelp)
        {
            Console.Out.WriteLine(CliArguments.UsageText);
            return 0;
        }
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(parsed.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliArguments.UsageText);
            return 1;
        }

        var configPath = AppSettingsLoader.ResolveConfigPath(parsed.ConfigPath);
        if (configPath is null)
        {
            Console.Error.WriteLine(
                "appsettings.json not found. Tried --config, ./appsettings.json, and the executable directory.");
            return 2;
        }

        AppSettings settings;
        try { settings = AppSettingsLoader.Load(configPath); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to load config: " + ex.Message);
            return 2;
        }

        var logsFolder = AppSettingsLoader.ResolvePath(settings, settings.Paths.Logs);
        var consoleEnabled = !parsed.Quiet;
        await using var logger = CliLogger.Create(settings.Logging, logsFolder, consoleEnabled);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (sender, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
                try
                {
                    Console.Out.WriteLine();
                    Console.Out.WriteLine("Cancellation requested - waiting for current work to finish...");
                }
                catch { }
            }
        };
        Console.CancelKeyPress += handler;

        try
        {
            IBacktestFacade facade = new BacktestFacade(settings);

            if (parsed.Daemon)
            {
                if (parsed.HadIgnoredFlagsInDaemonMode)
                    logger.Warning("Flags other than --config are ignored in --daemon mode.");
                return await DaemonRunner.RunAsync(settings, logger, facade, cts.Token)
                                         .ConfigureAwait(false);
            }

            return CliRunner.Run(settings, logger, parsed, facade, cts.Token);
        }
        catch (Exception ex)
        {
            logger.Fatal("Unhandled exception at top level.", ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}