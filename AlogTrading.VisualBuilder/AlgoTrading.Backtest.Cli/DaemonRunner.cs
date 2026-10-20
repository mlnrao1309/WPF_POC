using System.Threading.Channels;
using AlgoTrading.Backtest.Cli.Bridge;

namespace AlgoTrading.Backtest.Cli;

public static class DaemonRunner
{
    private const double PollIntervalSeconds = 0.5;

    public static async Task<int> RunAsync(AppSettings settings, CliLogger logger,
                                           IBacktestFacade facade,
                                           CancellationToken cancellationToken)
    {
        var pending = AppSettingsLoader.ResolvePath(settings, settings.Paths.RulesPending);
        var running = AppSettingsLoader.ResolvePath(settings, settings.Paths.RulesRunning);
        var done    = AppSettingsLoader.ResolvePath(settings, settings.Paths.RulesDone);
        var failed  = AppSettingsLoader.ResolvePath(settings, settings.Paths.RulesFailed);
        var results = AppSettingsLoader.ResolvePath(settings, settings.Paths.Results);
        var lockPath = AppSettingsLoader.ResolvePath(settings, settings.Paths.Lock);

        Directory.CreateDirectory(pending);
        Directory.CreateDirectory(running);
        Directory.CreateDirectory(done);
        Directory.CreateDirectory(failed);
        Directory.CreateDirectory(results);

        LockFile lockFile;
        try { lockFile = LockFile.Acquire(lockPath); }
        catch (Exception ex) { logger.Fatal("Failed to acquire daemon lock.", ex); return 1; }

        int completed = 0;
        int failedCount = 0;
        string exitReason = "shutdown";

        try
        {
            logger.Information("Daemon started. pending=" + pending + " results=" + results +
                               " idleExit=" + settings.Daemon.IdleExitSeconds + "s");

            RecoverRunningFolder(running, pending, logger);
            PurgeOrphanNotify(results, logger);

            var writer = new ResultWriter(results);
            var processor = new JobProcessor(logger, facade, writer, settings.Backtest.MaxParallelism);

            using var watcher = new FileWatcher(pending);
            watcher.Start();

            try
            {
                int idleSeconds = settings.Daemon.IdleExitSeconds;
                double idleAccumulator = 0.0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? jobPath = null;
                    bool channelClosed = false;

                    using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        waitCts.CancelAfter(TimeSpan.FromMilliseconds(PollIntervalSeconds * 1000.0));
                        try
                        {
                            bool hasData = await watcher.Reader.WaitToReadAsync(waitCts.Token)
                                .ConfigureAwait(false);
                            if (!hasData)
                            {
                                channelClosed = true;
                            }
                            else
                            {
                                watcher.Reader.TryRead(out jobPath);
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Poll timeout -- treat as no job this cycle.
                        }
                    }

                    if (channelClosed) break;
                    if (cancellationToken.IsCancellationRequested) break;

                    if (jobPath is not null)
                    {
                        idleAccumulator = 0.0;
                        if (File.Exists(jobPath))
                        {
                            var ok = ProcessOneJob(processor, jobPath, pending, running, done, failed, logger);
                            if (ok) completed++; else failedCount++;
                        }
                        continue;
                    }

                    // No job in this cycle. Consult the filesystem, not the
                    // channel, so that a job dropped in pending/ a moment ago
                    // but not yet through the stability check is respected.
                    bool queueEmpty = !HasPendingJobs(pending) && !HasPendingJobs(running);
                    if (queueEmpty)
                    {
                        idleAccumulator += PollIntervalSeconds;
                        if (idleAccumulator >= idleSeconds)
                        {
                            logger.Information("Queue empty for " + idleSeconds + "s. Exiting.");
                            exitReason = "idle";
                            break;
                        }
                    }
                    else
                    {
                        idleAccumulator = 0.0;
                    }
                }
            }
            finally
            {
                logger.Information("Daemon stopping (" + exitReason + "). completed=" + completed +
                                   " failed=" + failedCount + ".");
            }
        }
        finally
        {
            lockFile.Dispose();
        }

        return cancellationToken.IsCancellationRequested ? 130 : 0;
    }

    private static bool HasPendingJobs(string folder)
    {
        try
        {
            foreach (var _ in Directory.EnumerateFiles(folder, "*.job.json"))
                return true;
        }
        catch { }
        return false;
    }

    private static bool ProcessOneJob(JobProcessor processor, string jobPath,
                                      string pending, string running, string done, string failed,
                                      CliLogger logger)
    {
        var jobFileName = Path.GetFileName(jobPath);
        var baseName = jobFileName.EndsWith(".job.json", StringComparison.OrdinalIgnoreCase)
            ? jobFileName[..^".job.json".Length]
            : Path.GetFileNameWithoutExtension(jobFileName);
        var strategyFileName = baseName + ".json";
        var strategyPath = Path.Combine(pending, strategyFileName);

        var runJobPath = Path.Combine(running, jobFileName);
        var runStrategyPath = Path.Combine(running, strategyFileName);

        try
        {
            if (!File.Exists(strategyPath))
                throw new FileNotFoundException(
                    "Companion strategy file not found: " + strategyFileName, strategyPath);
            MoveFile(jobPath, runJobPath);
            MoveFile(strategyPath, runStrategyPath);
        }
        catch (Exception ex)
        {
            logger.Error("Failed to move job " + jobFileName + " to running/", ex);
            TryMoveBothToFailed(jobPath, strategyPath, failed, logger, ex);
            return false;
        }

        try
        {
            var outcome = processor.Process(runJobPath, strategyFileName);

            if (outcome.Success)
            {
                MoveIfExists(runJobPath, Path.Combine(done, jobFileName), logger);
                MoveIfExists(runStrategyPath, Path.Combine(done, strategyFileName), logger);
                logger.Information("Job completed: " + jobFileName + " -> " + outcome.ResultBaseName);
                return true;
            }

            var errorFile = Path.Combine(failed, jobFileName + ".error.txt");
            TryWriteErrorFile(errorFile, outcome.ErrorMessage ?? "Unknown error");
            MoveIfExists(runJobPath, Path.Combine(failed, jobFileName), logger);
            MoveIfExists(runStrategyPath, Path.Combine(failed, strategyFileName), logger);
            logger.Error("Job failed: " + jobFileName + " - " + outcome.ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            logger.Error("Job threw: " + jobFileName, ex);
            TryWriteErrorFile(Path.Combine(failed, jobFileName + ".error.txt"), ex.ToString());
            MoveIfExists(runJobPath, Path.Combine(failed, jobFileName), logger);
            MoveIfExists(runStrategyPath, Path.Combine(failed, strategyFileName), logger);
            return false;
        }
    }

    private static void RecoverRunningFolder(string running, string pending, CliLogger logger)
    {
        try
        {
            var files = new List<string>();
            foreach (var f in Directory.EnumerateFiles(running, "*", SearchOption.TopDirectoryOnly))
                files.Add(f);
            if (files.Count == 0) return;

            logger.Warning("Recovering " + files.Count + " file(s) from running/ -> pending/");
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var target = Path.Combine(pending, name);
                try { MoveFile(file, target); }
                catch (Exception ex) { logger.Warning("Recovery failed for " + name + ": " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            logger.Warning("Recovery scan failed: " + ex.Message);
        }
    }

    private static void PurgeOrphanNotify(string resultsFolder, CliLogger logger)
    {
        try
        {
            foreach (var notify in Directory.EnumerateFiles(resultsFolder, "*.notify"))
            {
                var pointer = notify[..^".notify".Length];
                if (!File.Exists(pointer))
                {
                    try { File.Delete(notify); logger.Debug("Deleted orphan notify: " + Path.GetFileName(notify)); }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void MoveFile(string source, string destination)
    {
        if (File.Exists(destination))
        {
            var dir = Path.GetDirectoryName(destination) ?? ".";
            var name = Path.GetFileNameWithoutExtension(destination);
            var ext = Path.GetExtension(destination);
            var unique = Path.Combine(dir, name + "." +
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ext);
            File.Move(source, unique);
            return;
        }
        File.Move(source, destination);
    }

    private static void MoveIfExists(string source, string destination, CliLogger logger)
    {
        try
        {
            if (!File.Exists(source)) return;
            MoveFile(source, destination);
        }
        catch (Exception ex)
        {
            logger.Warning("Move failed: " + source + " -> " + destination + ": " + ex.Message);
        }
    }

    private static void TryMoveBothToFailed(string jobPath, string strategyPath,
                                            string failed, CliLogger logger, Exception ex)
    {
        try
        {
            if (File.Exists(jobPath))
            {
                TryWriteErrorFile(Path.Combine(failed, Path.GetFileName(jobPath) + ".error.txt"), ex.ToString());
                MoveFile(jobPath, Path.Combine(failed, Path.GetFileName(jobPath)));
            }
            if (File.Exists(strategyPath))
                MoveFile(strategyPath, Path.Combine(failed, Path.GetFileName(strategyPath)));
        }
        catch (Exception inner)
        {
            logger.Warning("Fallback failed-move also failed: " + inner.Message);
        }
    }

    private static void TryWriteErrorFile(string path, string contents)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, contents);
        }
        catch { }
    }
}