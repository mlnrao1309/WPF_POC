using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AlgoTrading.RuleEditor.Services;

/// <summary>
/// Detects whether the CLI daemon is running (lock-file probe with PID-reuse
/// guard) and, if not, spawns it detached with --daemon --config. Does not
/// capture stdout: the daemon writes its own log (Section 6.8).
/// </summary>
public sealed class DaemonLauncherService
{
    private readonly string _cliExePath;
    private readonly string _configPath;
    private readonly string _lockPath;

    public DaemonLauncherService(string cliExePath, string configPath, string lockPath)
    {
        _cliExePath = cliExePath;
        _configPath = configPath;
        _lockPath = lockPath;
    }

    public bool CliExists => File.Exists(_cliExePath);

    public bool IsRunning() => LockFileProbe.IsHeldByLiveProcess(_lockPath);

    /// <summary>
    /// Ensures the daemon is running. If already running, returns true
    /// immediately. Otherwise spawns it and waits up to waitMs for the lock
    /// file to appear. Returns false with a diagnostic message on failure.
    /// </summary>
    public bool EnsureRunning(int waitMs, out string error)
    {
        error = "";

        if (IsRunning()) return true;

        if (!CliExists)
        {
            error = "CLI executable not found at " + _cliExePath +
                    ". Build the CLI in Release mode or set Paths.LibraryRoot in appsettings.json.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cliExePath,
                Arguments = "--daemon --config \"" + _configPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_cliExePath) ?? "."
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            error = "Failed to start daemon: " + ex.Message;
            return false;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs)
        {
            if (IsRunning()) return true;
            System.Threading.Thread.Sleep(150);
        }

        error = "Daemon did not acquire lock within " + waitMs + " ms. Check logs.";
        return false;
    }
}

internal static class LockFileProbe
{
    public static bool IsHeldByLiveProcess(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath)) return false;
            var text = ReadAllTextShared(lockPath);
            if (string.IsNullOrWhiteSpace(text)) return false;

            var dto = JsonSerializer.Deserialize<LockPayload>(text);
            if (dto is null || dto.Pid <= 0) return false;

            using var proc = Process.GetProcessById(dto.Pid);
            var actualStart = proc.StartTime.ToUniversalTime();
            var diff = (actualStart - dto.StartedAtUtc).Duration();
            return diff < TimeSpan.FromSeconds(5);
        }
        catch
        {
            return false;
        }
    }
    private static string ReadAllTextShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
    private sealed class LockPayload
    {
        public int Pid { get; set; }
        public DateTime StartedAtUtc { get; set; }
    }
}