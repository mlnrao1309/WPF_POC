using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace AlgoTrading.Backtest.Cli;

public enum LogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4
}

/// <summary>
/// Hand-rolled async logger. Zero third-party dependencies.
/// - IST timestamps (per decision #8).
/// - Redacts password/token/apikey/secret/connectionstring patterns.
/// - Writes logs/backtest-YYYY-MM-DD.log; rotates to .log.1 on size or day change.
/// - Retains the last N days (config-driven).
/// - Console mirroring can be disabled (--quiet).
/// </summary>
public sealed class CliLogger : IAsyncDisposable
{
    private static readonly TimeZoneInfo IstZone = ResolveIstTimeZone();
    //private static readonly Regex RedactionRegex = new(
    //    @"(?i)\b(password|pwd|token|apikey|api_key|secret|connectionstring)\s*=\s*[^;'""\s]+",
    //    RegexOptions.Compiled | RegexOptions.CultureInvariant);
    //private static readonly Regex RedactionRegex = new(
    //@"(?i)\b(password|pwd|apikey|api_key|secret|connectionstring)\s*=\s*[^;'""\s]+" +
    //@"|(?i)\btoken\s*=\s*[A-Za-z0-9_\-]{20,}",
    //RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Keyword=value secrets: always redacted.
    private static readonly Regex KeywordSecretRegex = new(
        @"(?i)\b(?<kw>password|pwd|apikey|api_key|secret|connectionstring)\s*=\s*[^;'""\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // token=<value>: redacted only when the value is non-numeric.
    private static readonly Regex AlphanumericTokenRegex = new(
        @"(?i)\b(?<kw>token)\s*=\s*[^0-9\s;'""][^;'""\s]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Channel<LogEntry> _channel;
    private readonly Task _drainTask;
    private readonly string _logFolder;
    private readonly int _retainDays;
    private readonly long _maxFileBytes;
    private readonly bool _consoleEnabled;
    private bool _disposed;

    public LogLevel MinimumLevel { get; }

    private CliLogger(LoggingSection settings, string logFolder, bool consoleEnabled)
    {
        MinimumLevel = ParseLevel(settings.Level);
        _logFolder = Path.GetFullPath(logFolder);
        _retainDays = Math.Max(1, settings.RetainDays);
        _maxFileBytes = Math.Max(1, settings.MaxFileSizeMB) * 1024L * 1024L;
        _consoleEnabled = consoleEnabled;

        var opts = new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _channel = Channel.CreateBounded<LogEntry>(opts);

        Directory.CreateDirectory(_logFolder);
        PurgeOldLogs();

        _drainTask = Task.Run(DrainLoopAsync);
    }

    public static CliLogger Create(LoggingSection settings, string logFolder, bool consoleEnabled)
        => new(settings, logFolder, consoleEnabled);

    public void Debug(string message) => Enqueue(LogLevel.Debug, message, null);
    public void Information(string message) => Enqueue(LogLevel.Information, message, null);
    public void Warning(string message) => Enqueue(LogLevel.Warning, message, null);
    public void Error(string message, Exception? ex = null) => Enqueue(LogLevel.Error, message, ex);
    public void Fatal(string message, Exception? ex = null) => Enqueue(LogLevel.Fatal, message, ex);

    private void Enqueue(LogLevel level, string message, Exception? ex)
    {
        if (level < MinimumLevel) return;
        var entry = new LogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Message = message,
            Exception = ex
        };
        _channel.Writer.TryWrite(entry);
    }

    private async Task DrainLoopAsync()
    {
        FileStream? stream = null;
        StreamWriter? writer = null;
        DateTime currentFileDate = DateTime.MinValue;
        long currentFileSize = 0;

        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    var istTime = TimeZoneInfo.ConvertTimeFromUtc(entry.TimestampUtc, IstZone);
                    var istDate = istTime.Date;
                    var formatted = FormatEntry(entry, istTime);

                    if (stream is null || istDate != currentFileDate)
                    {
                        if (writer is not null)
                        {
                            try { await writer.FlushAsync().ConfigureAwait(false); } catch { }
                            writer.Dispose();
                        }
                        stream?.Dispose();
                        (stream, writer) = OpenLogFile(istDate);
                        currentFileDate = istDate;
                        currentFileSize = SafeLength(stream);
                    }
                    else if (currentFileSize > _maxFileBytes)
                    {
                        if (writer is not null)
                        {
                            try { await writer.FlushAsync().ConfigureAwait(false); } catch { }
                            writer.Dispose();
                        }
                        stream?.Dispose();
                        (stream, writer) = RotateLogFile(istDate);
                        currentFileDate = istDate;
                        currentFileSize = SafeLength(stream);
                    }

                    if (writer is not null)
                    {
                        await writer.WriteAsync(formatted).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                        currentFileSize = SafeLength(stream);
                    }

                    if (_consoleEnabled)
                    {
                        try { Console.Out.Write(formatted); } catch { }
                        if (entry.Level >= LogLevel.Error)
                        {
                            try { Console.Out.Flush(); } catch { }
                        }
                    }
                }
                catch (Exception writeEx)
                {
                    // Never crash the drain loop for a log write.
                    try { Console.Error.WriteLine("[log-failure] " + writeEx.Message); } catch { }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (writer is not null)
            {
                try { await writer.FlushAsync().ConfigureAwait(false); } catch { }
                writer.Dispose();
            }
            stream?.Dispose();
        }
    }

    private static long SafeLength(FileStream? fs)
    {
        try { return fs?.Length ?? 0; } catch { return 0; }
    }

    private static string FormatEntry(LogEntry entry, DateTime istTime)
    {
        var sb = new StringBuilder(200);
        sb.Append('[')
          .Append(istTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append(" IST] [")
          .Append(LevelLabel(entry.Level))
          .Append("] ")
          .Append(Redact(entry.Message))
          .Append('\n');

        if (entry.Exception is not null)
        {
            sb.Append("  Exception: ").Append(entry.Exception.GetType().FullName).Append('\n');
            sb.Append("  Message:   ").Append(Redact(entry.Exception.Message)).Append('\n');
            var st = entry.Exception.StackTrace;
            if (!string.IsNullOrWhiteSpace(st))
                sb.Append("  Stack:     ").Append(Redact(st)).Append('\n');
        }
        return sb.ToString();
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Fatal => "FTL",
        _ => "???"
    };

    private static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var s = KeywordSecretRegex.Replace(input, "${kw}=***");
        s = AlphanumericTokenRegex.Replace(s, "${kw}=***");
        return s;
    }

    private (FileStream, StreamWriter) OpenLogFile(DateTime istDate)
    {
        var name = "backtest-" + istDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
        var path = Path.Combine(_logFolder, name);
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 16384, FileOptions.SequentialScan);
        var sw = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = false };
        return (fs, sw);
    }

    private (FileStream, StreamWriter) RotateLogFile(DateTime istDate)
    {
        var name = "backtest-" + istDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
        var path = Path.Combine(_logFolder, name);
        var rotated = path + ".1";
        try
        {
            if (File.Exists(rotated)) File.Delete(rotated);
            if (File.Exists(path)) File.Move(path, rotated);
        }
        catch { }
        return OpenLogFile(istDate);
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-_retainDays);
            foreach (var file in Directory.EnumerateFiles(_logFolder, "backtest-*.log*"))
            {
                var name = Path.GetFileName(file);
                if (name.Length < 19) continue;
                var datePart = name.Substring(9, 10);
                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d) && d < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static LogLevel ParseLevel(string? s)
    {
        return s?.Trim().ToLowerInvariant() switch
        {
            "debug" or "verbose" => LogLevel.Debug,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "fatal" or "critical" => LogLevel.Fatal,
            _ => LogLevel.Information
        };
    }

    private static TimeZoneInfo ResolveIstTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("India Standard Time", out var tz1))
            return tz1;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Kolkata", out var tz2))
            return tz2;
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        try { await _drainTask.ConfigureAwait(false); } catch { }
    }

    private sealed class LogEntry
    {
        public DateTime TimestampUtc { get; init; }
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public Exception? Exception { get; init; }
    }
}