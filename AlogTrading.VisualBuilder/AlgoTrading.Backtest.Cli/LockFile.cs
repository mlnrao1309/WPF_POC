using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AlgoTrading.Backtest.Cli;

/// <summary>
/// Exclusive daemon lock (Section 6.7). Holds an open handle with FileShare.None.
/// Detects stale locks and PID reuse (5 s start-time tolerance).
/// </summary>
public sealed class LockFile : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;
    private bool _disposed;

    private LockFile(string path, FileStream stream)
    {
        _path = path;
        _stream = stream;
    }

    public static LockFile Acquire(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (TryCreate(full, out var stream, out _))
            return new LockFile(full, stream!);

        if (!TryReadInfo(full, out var info))
        {
            TryDelete(full);
            if (TryCreate(full, out stream, out var e2))
                return new LockFile(full, stream!);
            throw new InvalidOperationException(
                "Failed to acquire daemon lock after corrupt-file recovery: " + e2?.Message);
        }

        if (IsPidAlive(info.Pid, info.StartedAtUtc))
        {
            throw new InvalidOperationException(
                "Another daemon is already running (pid=" + info.Pid +
                ", started=" + info.StartedAtUtc.ToString("o") + ").");
        }

        TryDelete(full);
        if (TryCreate(full, out stream, out var e3))
            return new LockFile(full, stream!);
        throw new InvalidOperationException(
            "Failed to acquire daemon lock after stale-lock cleanup: " + e3?.Message);
    }

    private static bool TryCreate(string path, out FileStream? stream, out Exception? error)
    {
        try
        {
            var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read, bufferSize: 4096, FileOptions.WriteThrough);

            var payload = JsonSerializer.Serialize(new LockPayloadDto
            {
                Pid = Environment.ProcessId,
                StartedAtUtc = GetCurrentProcessStartUtc()
            });
            var bytes = Encoding.UTF8.GetBytes(payload);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);

            stream = fs;
            error = null;
            return true;
        }
        catch (IOException ex)
        {
            stream = null;
            error = ex;
            return false;
        }
    }
    private static string ReadAllTextShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
    private static bool TryReadInfo(string path, out LockInfo info)
    {
        info = new LockInfo(0, DateTime.MinValue);
        try
        {
            var text = ReadAllTextShared(path);
            if (string.IsNullOrWhiteSpace(text)) return false;
            var dto = JsonSerializer.Deserialize<LockPayloadDto>(text);
            if (dto is null || dto.Pid <= 0) return false;
            info = new LockInfo(dto.Pid, dto.StartedAtUtc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPidAlive(int pid, DateTime recordedStartUtc)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var actualStart = proc.StartTime.ToUniversalTime();
            var diff = (actualStart - recordedStartUtc).Duration();
            return diff < TimeSpan.FromSeconds(5);
        }
        catch
        {
            // No such process, access denied, or StartTime unavailable.
            return false;
        }
    }

    private static DateTime GetCurrentProcessStartUtc()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            return p.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stream?.Dispose(); } catch { }
        _stream = null;
        TryDelete(_path);
    }

    private readonly record struct LockInfo(int Pid, DateTime StartedAtUtc);

    private sealed class LockPayloadDto
    {
        public int Pid { get; set; }
        public DateTime StartedAtUtc { get; set; }
    }
}