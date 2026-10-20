using System.IO;
using System.Text.Json;

namespace AlgoTrading.RuleEditor.Services;

public sealed record ResultNotification
{
    public required string ResultPath { get; init; }
    public string? StrategyName { get; init; }
    public uint? InstrumentToken { get; init; }
    public int? TotalTrades { get; init; }
    public decimal? NetPnL { get; init; }
    public decimal? WinRate { get; init; }
}

/// <summary>
/// Watches results/*.notify (Section 8.16). When a .notify file appears, reads
/// the pointed-to result JSON, parses a compact summary, deletes the .notify,
/// and raises ResultArrived. Runs on a background thread; consumers must
/// marshal to the UI thread.
/// </summary>
public sealed class ResultWatcherService : IDisposable
{
    private readonly string _resultsFolder;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public event EventHandler<ResultNotification>? ResultArrived;

    public ResultWatcherService(string resultsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsFolder);
        _resultsFolder = Path.GetFullPath(resultsFolder);
        Directory.CreateDirectory(_resultsFolder);

        _watcher = new FileSystemWatcher(_resultsFolder, "*.notify")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = false
        };
        _watcher.Created += OnNotify;
        _watcher.Renamed += OnNotifyRenamed;
    }

    public void Start()
    {
        ScanExisting();
        _watcher.EnableRaisingEvents = true;
    }

    private void ScanExisting()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_resultsFolder, "*.notify"))
                ProcessNotify(f);
        }
        catch { }
    }

    private void OnNotify(object sender, FileSystemEventArgs e) => ProcessNotify(e.FullPath);
    private void OnNotifyRenamed(object sender, RenamedEventArgs e) => ProcessNotify(e.FullPath);

    private void ProcessNotify(string notifyPath)
    {
        try
        {
            // Small delay lets the OS settle; harmless when already stable.
            System.Threading.Thread.Sleep(60);
            if (!File.Exists(notifyPath)) return;

            var resultPath = File.ReadAllText(notifyPath).Trim();
            if (string.IsNullOrEmpty(resultPath) || !File.Exists(resultPath))
            {
                try { File.Delete(notifyPath); } catch { }
                return;
            }

            var notification = ParseResult(resultPath);
            try { File.Delete(notifyPath); } catch { }

            ResultArrived?.Invoke(this, notification);
        }
        catch { }
    }

    private static ResultNotification ParseResult(string resultPath)
    {
        var n = new ResultNotification { ResultPath = resultPath };
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(resultPath));
            var root = doc.RootElement;

            if (root.TryGetProperty("reproducibility", out var rep) &&
                rep.ValueKind == JsonValueKind.Object)
            {
                if (rep.TryGetProperty("strategyName", out var sn) &&
                    sn.ValueKind == JsonValueKind.String)
                    n = n with { StrategyName = sn.GetString() };
                if (rep.TryGetProperty("instrumentToken", out var it) &&
                    it.ValueKind == JsonValueKind.Number && it.TryGetUInt32(out var tok))
                    n = n with { InstrumentToken = tok };
            }

            if (root.TryGetProperty("metrics", out var m) &&
                m.ValueKind == JsonValueKind.Object)
            {
                if (m.TryGetProperty("totalTrades", out var tt) &&
                    tt.ValueKind == JsonValueKind.Number && tt.TryGetInt32(out var t))
                    n = n with { TotalTrades = t };
                if (m.TryGetProperty("netPnl", out var np) &&
                    np.ValueKind == JsonValueKind.Number && np.TryGetDecimal(out var p))
                    n = n with { NetPnL = p };
                if (m.TryGetProperty("winRate", out var wr) &&
                    wr.ValueKind == JsonValueKind.Number && wr.TryGetDecimal(out var w))
                    n = n with { WinRate = w };
            }
        }
        catch { }
        return n;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _watcher.EnableRaisingEvents = false; } catch { }
        try { _watcher.Dispose(); } catch { }
    }
}