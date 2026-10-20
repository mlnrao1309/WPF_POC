using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlgoTrading.RuleEditor.Services;

/// <summary>
/// Editor-side settings loaded from appsettings.json. Search order:
///   1. AppContext.BaseDirectory\appsettings.json (copied via linked file)
///   2. Walk up from AppContext.BaseDirectory (7 levels)
///   3. Current directory, walk up (7 levels)
///   4. %LocalAppData%\AlgoTrading\RuleEditor\appsettings.json
///   5. Auto-create (4) with defaults if nothing found.
/// Env var ALGOTRADING_SQL overrides Data.Sql.ConnectionString.
/// </summary>
public sealed class EditorSettings
{
    public string DataSource { get; init; } = "Sql";
    public string SqlConnectionString { get; init; } = "";
    public string RulesPendingPath { get; init; } = "";
    public string RulesRunningPath { get; init; } = "";
    public string StrategiesFolderPath { get; init; } = "";
    public string ResultsPath { get; init; } = "";
    public string CostsPath { get; init; } = "";
    public string MarginsPath { get; init; } = "";
    public string SlippagePath { get; init; } = "";
    public string LibraryRootPath { get; init; } = "";
    public string CliExecutablePath { get; init; } = "";
    public string LockPath { get; init; } = "";
    public string ConfigPath { get; init; } = "";
    public string BaseDirectory { get; init; } = "";

    public bool HasSql => !string.IsNullOrWhiteSpace(SqlConnectionString);
    public bool HasCli => File.Exists(CliExecutablePath);

    public static EditorSettings Load()
    {
        var path = ResolveConfigPath();
        return LoadFrom(path);
    }

    public static EditorSettings LoadFrom(string path)
    {
        var full = Path.GetFullPath(path);
        var baseDir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();

        try
        {
            var text = File.ReadAllText(full);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var envCs = Environment.GetEnvironmentVariable("ALGOTRADING_SQL");
            var sqlCs = !string.IsNullOrWhiteSpace(envCs)
                ? envCs!
                : GetStr(root, "Data", "Sql", "ConnectionString") ?? "";

            var projectRoot = FindProjectRoot(baseDir) ?? baseDir;
            var libraryRoot = ResolvePath(projectRoot,
                GetStr(root, "Paths", "LibraryRoot") ?? "Library");

            var cliExe = Path.Combine(libraryRoot, "bin", "net10.0",
                "AlgoTrading.Backtest.Cli.exe");

            return new EditorSettings
            {
                DataSource = GetStr(root, "Data", "Source") ?? "Sql",
                SqlConnectionString = sqlCs,
                RulesPendingPath = ResolvePath(baseDir,
                    GetStr(root, "Paths", "RulesPending") ?? "rules/pending"),
                RulesRunningPath = ResolvePath(baseDir,
                    GetStr(root, "Paths", "RulesRunning") ?? "rules/running"),
                StrategiesFolderPath = ResolvePath(baseDir,
                    GetStr(root, "Paths", "StrategiesFolder") ?? "strategies"),
                ResultsPath = ResolvePath(baseDir,
                    GetStr(root, "Paths", "Results") ?? "results"),
                CostsPath = ResolvePath(baseDir,
                    GetStr(root, "Configs", "CostsPath") ?? "configs/costs.json"),
                MarginsPath = ResolvePath(baseDir,
                    GetStr(root, "Configs", "MarginsPath") ?? "configs/margins.json"),
                SlippagePath = ResolvePath(baseDir,
                    GetStr(root, "Configs", "SlippagePath") ?? "configs/slippage.json"),
                LibraryRootPath = libraryRoot,
                CliExecutablePath = cliExe,
                LockPath = ResolvePath(baseDir,
                    GetStr(root, "Paths", "Lock") ?? "lock/daemon.lock"),
                ConfigPath = full,
                BaseDirectory = baseDir
            };
        }
        catch
        {
            return new EditorSettings
            {
                DataSource = "Sql",
                SqlConnectionString = Environment.GetEnvironmentVariable("ALGOTRADING_SQL") ?? "",
                RulesPendingPath = Path.Combine(baseDir, "rules", "pending"),
                RulesRunningPath = Path.Combine(baseDir, "rules", "running"),
                StrategiesFolderPath = Path.Combine(baseDir, "strategies"),
                ResultsPath = Path.Combine(baseDir, "results"),
                CostsPath = Path.Combine(baseDir, "configs", "costs.json"),
                MarginsPath = Path.Combine(baseDir, "configs", "margins.json"),
                SlippagePath = Path.Combine(baseDir, "configs", "slippage.json"),
                LibraryRootPath= Path.Combine(FindProjectRoot(baseDir) ?? baseDir, "Library"),
                CliExecutablePath = Path.Combine(FindProjectRoot(baseDir) ?? baseDir,
    "Library", "bin", "net10.0", "AlgoTrading.Backtest.Cli.exe"),
                LockPath = Path.Combine(baseDir, "lock", "daemon.lock"),
                ConfigPath = full,
                BaseDirectory = baseDir
            };
        }
    }

    private static string? FindProjectRoot(string startDir)
    {
        var d = Path.GetFullPath(startDir).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < 10; i++)
        {
            if (string.IsNullOrEmpty(d)) return null;
            if (File.Exists(Path.Combine(d, "algotrading.slnx"))) return d;
            var parent = Path.GetDirectoryName(d);
            if (string.IsNullOrEmpty(parent) || parent == d) return null;
            d = parent;
        }
        return null;
    }

    public static void SaveSqlConnectionString(string configPath, string connectionString)
    {
        JsonObject root;
        if (File.Exists(configPath))
        {
            var text = File.ReadAllText(configPath);
            root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["Data"] is not JsonObject data)
        {
            data = new JsonObject();
            root["Data"] = data;
        }
        if (data["Sql"] is not JsonObject sql)
        {
            sql = new JsonObject();
            data["Sql"] = sql;
        }
        sql["ConnectionString"] = connectionString;

        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, root.ToJsonString(opts));
    }

    private static string ResolveConfigPath()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryWalkUp(AppContext.BaseDirectory, 7, visited, out var found)) return found;
        if (TryWalkUp(Directory.GetCurrentDirectory(), 7, visited, out found)) return found;

        var local = LocalAppDataPath();
        if (File.Exists(local)) return local;

        var dirLocal = Path.GetDirectoryName(local)!;
        Directory.CreateDirectory(dirLocal);
        File.WriteAllText(local, DefaultConfigJson);
        return local;
    }

    private static bool TryWalkUp(string start, int maxLevels,
                                  HashSet<string> visited, out string found)
    {
        found = "";
        var d = Path.GetFullPath(start).TrimEnd(Path.DirectorySeparatorChar,
                                                Path.AltDirectorySeparatorChar);
        for (int i = 0; i < maxLevels; i++)
        {
            if (string.IsNullOrEmpty(d)) break;
            if (!visited.Add(d)) break;

            var candidate = Path.Combine(d, "appsettings.json");
            if (File.Exists(candidate)) { found = candidate; return true; }

            var parent = Path.GetDirectoryName(d);
            if (string.IsNullOrEmpty(parent) || parent == d) break;
            d = parent;
        }
        return false;
    }

    private static string LocalAppDataPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlgoTrading", "RuleEditor", "appsettings.json");
    }

    private const string DefaultConfigJson = """
    {
      "Data": {
        "Source": "Sql",
        "Sql": { "ConnectionString": "" },
        "Csv": { "FolderPath": "" },
        "Parquet": { "FolderPath": "" }
      },
      "Paths": {
        "RulesPending": "rules/pending",
        "RulesRunning": "rules/running",
        "RulesDone": "rules/done",
        "RulesFailed": "rules/failed",
        "StrategiesFolder": "strategies",
        "Results": "results",
        "Logs": "logs",
        "Lock": "lock/daemon.lock",
        "LibraryRoot": "Library"
      },
      "Configs": {
        "CostsPath": "configs/costs.json",
        "MarginsPath": "configs/margins.json",
        "SlippagePath": "configs/slippage.json"
      },
      "Backtest": {
        "InitialCapital": 100000,
        "RiskFreeRatePercent": 2.0,
        "MaxSweepCombinations": 10000,
        "MaxParallelism": 3,
        "WalkForward": { "InSampleRatio": 0.7, "WindowCount": 5 }
      },
      "Daemon": { "IdleExitSeconds": 30 },
      "Logging": { "Level": "Information", "RetainDays": 30, "MaxFileSizeMB": 20 }
    }
    """;

    private static string? GetStr(JsonElement root, params string[] path)
    {
        var cur = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (cur.ValueKind != JsonValueKind.Object) return null;
            if (!cur.TryGetProperty(path[i], out cur)) return null;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
    }

    private static string ResolvePath(string baseDir, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return baseDir;
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDir, path));
    }
}