using System.Globalization;
using System.Text;
using AlgoTrading.Backtest.Cli.Bridge;

namespace AlgoTrading.Backtest.Cli;

public sealed class WriteOutcome
{
    public required string ResultPath { get; init; }
    public required string TradesPath { get; init; }
    public required string SummaryPath { get; init; }
    public required string NotifyPath { get; init; }
}

/// <summary>
/// Writes the three result artifacts plus the .notify pointer (Section 6.12/6.5.4).
/// Atomic: write to .tmp, flush to disk, then move into place.
/// </summary>
public sealed class ResultWriter
{
    private readonly string _resultsFolder;

    public ResultWriter(string resultsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsFolder);
        _resultsFolder = Path.GetFullPath(resultsFolder);
        Directory.CreateDirectory(_resultsFolder);
    }

    public static string BuildBaseName(string strategyName, uint instrumentToken,
                                       DateOnly from, DateOnly to, DateTime utcNow)
    {
        var safeName = SanitizeName(strategyName);
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ts = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        return safeName + "_" + instrumentToken.ToString(CultureInfo.InvariantCulture)
               + "_" + fromStr + "_" + toStr + "_" + ts;
    }

    public WriteOutcome Write(string baseName, FacadeResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(result);

        var resultPath = Path.Combine(_resultsFolder, baseName + "_result.json");
        var tradesPath = Path.Combine(_resultsFolder, baseName + "_trades.csv");
        var summaryPath = Path.Combine(_resultsFolder, baseName + "_summary.txt");
        var notifyPath = resultPath + ".notify";

        WriteAtomic(resultPath, result.ResultJson ?? "{}", trailingNewline: true);
        WriteAtomic(tradesPath, result.TradesCsv ?? "", trailingNewline: false);
        WriteAtomic(summaryPath, result.SummaryText ?? "", trailingNewline: true);
        WriteAtomic(notifyPath, resultPath, trailingNewline: false);

        return new WriteOutcome
        {
            ResultPath = resultPath,
            TradesPath = tradesPath,
            SummaryPath = summaryPath,
            NotifyPath = notifyPath
        };
    }

    private static void WriteAtomic(string path, string contents, bool trailingNewline)
    {
        var tmp = path + ".tmp";
        var utf8 = new UTF8Encoding(false);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 16384, FileOptions.SequentialScan))
        using (var sw = new StreamWriter(fs, utf8))
        {
            sw.Write(contents);
            if (trailingNewline && !contents.EndsWith('\n')) sw.Write('\n');
            sw.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "strategy";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }
        var s = sb.ToString();
        return s.Length > 60 ? s[..60] : s;
    }
}