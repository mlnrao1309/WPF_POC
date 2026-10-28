using System.Text.Json;

namespace AlgoTrading.Backtest.Cli;

public sealed class AppSettings
{
    public required DataSection Data { get; init; }
    public required PathsSection Paths { get; init; }
    public required ConfigsSection Configs { get; init; }
    public required BacktestSection Backtest { get; init; }
    public required DaemonSection Daemon { get; init; }
    public required LoggingSection Logging { get; init; }

    /// <summary>Absolute directory containing the loaded appsettings.json.</summary>
    public required string BaseDirectory { get; init; }

    /// <summary>Absolute path of the loaded appsettings.json.</summary>
    public required string ConfigPath { get; init; }
}

public sealed class DataSection
{
    public string Source { get; init; } = "Sql";
    public SqlSection Sql { get; init; } = new();
    public CsvSection Csv { get; init; } = new();
    public ParquetSection Parquet { get; init; } = new();
}

public sealed class SqlSection { public string ConnectionString { get; init; } = ""; }
public sealed class CsvSection { public string FolderPath { get; init; } = ""; }
public sealed class ParquetSection { public string FolderPath { get; init; } = ""; }

public sealed class PathsSection
{
    public string RulesPending { get; init; } = "rules/pending";
    public string RulesRunning { get; init; } = "rules/running";
    public string RulesDone { get; init; } = "rules/done";
    public string RulesFailed { get; init; } = "rules/failed";
    public string StrategiesFolder { get; init; } = "strategies";
    public string Results { get; init; } = "results";
    public string Logs { get; init; } = "logs";
    public string Lock { get; init; } = "lock/daemon.lock";

    /// <summary>
    /// Root folder for release binaries (Delta 01.4). Resolved against the
    /// directory containing appsettings.json. The editor uses it to locate
    /// the CLI executable at <LibraryRoot>\bin\net10.0\.
    /// </summary>
    public string LibraryRoot { get; init; } = "Library";
}

public sealed class ConfigsSection
{
    public string CostsPath { get; init; } = "configs/costs.json";
    public string MarginsPath { get; init; } = "configs/margins.json";
    public string SlippagePath { get; init; } = "configs/slippage.json";
}

public sealed class BacktestSection
{
    public decimal InitialCapital { get; init; } = 100_000m;
    public decimal RiskFreeRatePercent { get; init; } = 2.0m;
    public int MaxSweepCombinations { get; init; } = 10_000;

    /// <summary>
    /// Upper bound on threads used by Parallel.ForEach inside a sweep
    /// (Delta 01.1, 01.4). Clamped at load time to
    /// [1, Environment.ProcessorCount - 1].
    /// </summary>
    public int MaxParallelism { get; init; } = 3;

    public WalkForwardSection WalkForward { get; init; } = new();
}

public sealed class DaemonSection
{
    /// <summary>
    /// Idle timeout before the daemon self-terminates (Delta 01.2, 01.4).
    /// Counted only while both pending/ and running/ are empty.
    /// Clamped at load time to >= 1.
    /// </summary>
    public int IdleExitSeconds { get; init; } = 120;
}

public sealed class WalkForwardSection
{
    public double InSampleRatio { get; init; } = 0.7;
    public int WindowCount { get; init; } = 5;
}

public sealed class LoggingSection
{
    public string Level { get; init; } = "Information";
    public int RetainDays { get; init; } = 30;
    public int MaxFileSizeMB { get; init; } = 20;
}

public static class AppSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Section 6.11 precedence. Returns null if nothing was found.</summary>
    public static string? ResolveConfigPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(cwd)) return Path.GetFullPath(cwd);

        var exeDir = AppContext.BaseDirectory;
        var exePath = Path.Combine(exeDir, "appsettings.json");
        if (File.Exists(exePath)) return Path.GetFullPath(exePath);

        return null;
    }

    public static AppSettings Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var text = File.ReadAllText(configPath);
        var dto = JsonSerializer.Deserialize<AppSettingsDto>(text, JsonOpts)
                  ?? throw new InvalidOperationException("appsettings.json is empty or null.");

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath))
                      ?? Directory.GetCurrentDirectory();

        // Delta 01.4: clamp MaxParallelism to [1, ProcessorCount - 1].
        var rawParallelism = dto.Backtest?.MaxParallelism ?? 3;
        var parallelismUpperBound = Math.Max(1, Environment.ProcessorCount - 1);
        var clampedParallelism = Math.Clamp(rawParallelism, 1, parallelismUpperBound);

        // Delta 01.4: clamp IdleExitSeconds to >= 1.
        var rawIdleExit = dto.Daemon?.IdleExitSeconds ?? 30;
        var clampedIdleExit = Math.Max(1, rawIdleExit);

        return new AppSettings
        {
            Data = new DataSection
            {
                Source = dto.Data?.Source ?? "Sql",
                Sql = new SqlSection { ConnectionString = dto.Data?.Sql?.ConnectionString ?? "" },
                Csv = new CsvSection { FolderPath = dto.Data?.Csv?.FolderPath ?? "" },
                Parquet = new ParquetSection { FolderPath = dto.Data?.Parquet?.FolderPath ?? "" }
            },
            Paths = new PathsSection
            {
                RulesPending = dto.Paths?.RulesPending ?? "rules/pending",
                RulesRunning = dto.Paths?.RulesRunning ?? "rules/running",
                RulesDone = dto.Paths?.RulesDone ?? "rules/done",
                RulesFailed = dto.Paths?.RulesFailed ?? "rules/failed",
                StrategiesFolder = dto.Paths?.StrategiesFolder ?? "strategies",
                Results = dto.Paths?.Results ?? "results",
                Logs = dto.Paths?.Logs ?? "logs",
                Lock = dto.Paths?.Lock ?? "lock/daemon.lock",
                LibraryRoot = dto.Paths?.LibraryRoot ?? "Library"
            },
            Configs = new ConfigsSection
            {
                CostsPath = dto.Configs?.CostsPath ?? "configs/costs.json",
                MarginsPath = dto.Configs?.MarginsPath ?? "configs/margins.json",
                SlippagePath = dto.Configs?.SlippagePath ?? "configs/slippage.json"
            },
            Backtest = new BacktestSection
            {
                InitialCapital = dto.Backtest?.InitialCapital ?? 100_000m,
                RiskFreeRatePercent = dto.Backtest?.RiskFreeRatePercent ?? 2.0m,
                MaxSweepCombinations = dto.Backtest?.MaxSweepCombinations ?? 10_000,
                MaxParallelism = clampedParallelism,
                WalkForward = new WalkForwardSection
                {
                    InSampleRatio = dto.Backtest?.WalkForward?.InSampleRatio ?? 0.7,
                    WindowCount = dto.Backtest?.WalkForward?.WindowCount ?? 5
                }
            },
            Daemon = new DaemonSection { IdleExitSeconds = clampedIdleExit },
            Logging = new LoggingSection
            {
                Level = dto.Logging?.Level ?? "Information",
                RetainDays = dto.Logging?.RetainDays ?? 30,
                MaxFileSizeMB = dto.Logging?.MaxFileSizeMB ?? 20
            },
            BaseDirectory = baseDir,
            ConfigPath = Path.GetFullPath(configPath)
        };
    }

    /// <summary>Resolve a possibly-relative path against the appsettings directory.</summary>
    public static string ResolvePath(AppSettings settings, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return settings.BaseDirectory;
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(settings.BaseDirectory, path));
    }
}

internal sealed class AppSettingsDto
{
    public DataDto? Data { get; set; }
    public PathsDto? Paths { get; set; }
    public ConfigsDto? Configs { get; set; }
    public BacktestDto? Backtest { get; set; }
    public DaemonDto? Daemon { get; set; }
    public LoggingDto? Logging { get; set; }
}

internal sealed class DataDto
{
    public string? Source { get; set; }
    public SqlDto? Sql { get; set; }
    public CsvDto? Csv { get; set; }
    public ParquetDto? Parquet { get; set; }
}

internal sealed class SqlDto { public string? ConnectionString { get; set; } }
internal sealed class CsvDto { public string? FolderPath { get; set; } }
internal sealed class ParquetDto { public string? FolderPath { get; set; } }

internal sealed class PathsDto
{
    public string? RulesPending { get; set; }
    public string? RulesRunning { get; set; }
    public string? RulesDone { get; set; }
    public string? RulesFailed { get; set; }
    public string? StrategiesFolder { get; set; }
    public string? Results { get; set; }
    public string? Logs { get; set; }
    public string? Lock { get; set; }
    public string? LibraryRoot { get; set; }
}

internal sealed class ConfigsDto
{
    public string? CostsPath { get; set; }
    public string? MarginsPath { get; set; }
    public string? SlippagePath { get; set; }
}

internal sealed class BacktestDto
{
    public decimal? InitialCapital { get; set; }
    public decimal? RiskFreeRatePercent { get; set; }
    public int? MaxSweepCombinations { get; set; }
    public int? MaxParallelism { get; set; }
    public WalkForwardDto? WalkForward { get; set; }
}

internal sealed class DaemonDto
{
    public int? IdleExitSeconds { get; set; }
}

internal sealed class WalkForwardDto
{
    public double? InSampleRatio { get; set; }
    public int? WindowCount { get; set; }
}

internal sealed class LoggingDto
{
    public string? Level { get; set; }
    public int? RetainDays { get; set; }
    public int? MaxFileSizeMB { get; set; }
}