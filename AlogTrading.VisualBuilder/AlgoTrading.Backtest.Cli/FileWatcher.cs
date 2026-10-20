using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AlgoTrading.Backtest.Cli;

/// <summary>
/// Watches a folder for stable *.job.json files and enqueues their full paths.
/// Stability: file size must be unchanged for stabilityMs.
/// Coalesces duplicate Created/Changed/Renamed events for the same path.
/// Enqueues in lexicographic order of the full path (Delta 01.3).
/// </summary>
public sealed class FileWatcher : IDisposable
{
    private readonly string _folder;
    private readonly int _stabilityMs;
    private readonly Channel<string> _channel;
    private readonly ConcurrentDictionary<string, FileCandidate> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _enqueued = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _scanTask;
    private bool _disposed;

    public FileWatcher(string folder, int stabilityMs = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = Path.GetFullPath(folder);
        _stabilityMs = Math.Max(100, stabilityMs);

        var opts = new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };
        _channel = Channel.CreateBounded<string>(opts);

        Directory.CreateDirectory(_folder);

        _watcher = new FileSystemWatcher(_folder, "*.job.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = false,
            InternalBufferSize = 64 * 1024
        };
        _watcher.Created += OnFsEvent;
        _watcher.Changed += OnFsEvent;
        _watcher.Renamed += OnFsRenamed;

        _scanTask = Task.Run(ScanLoopAsync);
    }

    public ChannelReader<string> Reader => _channel.Reader;

    public void Start()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*.job.json"))
            Track(file);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Track(e.FullPath);

    private void OnFsRenamed(object sender, RenamedEventArgs e)
    {
        _candidates.TryRemove(e.OldFullPath, out _);
        _enqueued.TryRemove(e.OldFullPath, out _);
        Track(e.FullPath);
    }

    private void Track(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            long size = -1;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) size = info.Length;
            }
            catch { }

            _candidates[path] = new FileCandidate(DateTime.UtcNow, size);
            _enqueued.TryRemove(path, out _);
        }
        catch { }
    }

    private async Task ScanLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                var now = DateTime.UtcNow;

                // Delta 01.3: deterministic lexicographic order of candidates.
                var snapshot = _candidates.ToArray();
                Array.Sort(snapshot, static (a, b) => string.CompareOrdinal(a.Key, b.Key));

                foreach (var kv in snapshot)
                {
                    var path = kv.Key;
                    var cand = kv.Value;

                    if (!File.Exists(path))
                    {
                        _candidates.TryRemove(path, out _);
                        _enqueued.TryRemove(path, out _);
                        continue;
                    }

                    long size;
                    try { size = new FileInfo(path).Length; }
                    catch { continue; }

                    if (size != cand.Size)
                    {
                        _candidates[path] = new FileCandidate(now, size);
                        _enqueued.TryRemove(path, out _);
                        continue;
                    }

                    if ((now - cand.LastChangeUtc).TotalMilliseconds < _stabilityMs)
                        continue;

                    if (_candidates.TryRemove(path, out _))
                    {
                        if (_enqueued.TryAdd(path, 0))
                        {
                            try { await _channel.Writer.WriteAsync(path, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { return; }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _watcher.EnableRaisingEvents = false; } catch { }
        try { _watcher.Dispose(); } catch { }

        try { _cts.Cancel(); } catch { }
        try { _scanTask.GetAwaiter().GetResult(); } catch { }

        try { _cts.Dispose(); } catch { }
    }

    private sealed record FileCandidate(DateTime LastChangeUtc, long Size);
}