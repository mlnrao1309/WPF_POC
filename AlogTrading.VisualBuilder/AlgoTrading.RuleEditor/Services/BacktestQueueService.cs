using System.Globalization;
using System.IO;
using System.Text;
using AlgoTrading.Models;

namespace AlgoTrading.RuleEditor.Services;

public sealed class QueueBatchRequest
{
    public required RuleDefinition Rule { get; init; }
    public required IReadOnlyList<uint> InstrumentTokens { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required string Mode { get; init; } // Single | Sweep | WalkForward
    public RiskLossBasis LossBasis { get; init; } = RiskLossBasis.RealizedOnly;
    public decimal TradedCapital { get; init; } = 50_000m;
}

public sealed class QueueBatchResult
{
    public required IReadOnlyList<string> QueuedJobPaths { get; init; }
    public required IReadOnlyList<uint> SkippedDuplicates { get; init; }
}

/// <summary>
/// Writes job + strategy files to the daemon's pending folder
/// (Section 6.4.3, Delta 01.3). Base name is
/// <strategyName>_<evaluationTimeframe>_<token>. Dedupes against pending/
/// and running/ only (Delta 01.3, 6.5.5). Editor does not insert into
/// BacktestRuns; the daemon owns that row.
/// </summary>
public sealed class BacktestQueueService
{
    private readonly string _pendingFolder;
    private readonly string _runningFolder;

    public BacktestQueueService(string pendingFolder, string runningFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(runningFolder);
        _pendingFolder = Path.GetFullPath(pendingFolder);
        _runningFolder = Path.GetFullPath(runningFolder);
    }

    public QueueBatchResult Queue(QueueBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.LossBasis) || request.TradedCapital <= 0m)
            throw new ArgumentException("A valid loss basis and positive traded capital are required.", nameof(request));
        Directory.CreateDirectory(_pendingFolder);
        Directory.CreateDirectory(_runningFolder);

        var evalTf = SafeTf(request.Rule.EvaluationTimeframe);
        var strategyName = RuleFileService.SanitizeName(request.Rule.StrategyName ?? "strategy");
        var ruleJson = RuleDefinitionJson.Serialize(request.Rule);

        var queued = new List<string>(request.InstrumentTokens.Count);
        var skipped = new List<uint>();

        foreach (var token in request.InstrumentTokens)
        {
            if (token == 0) continue;

            var baseName = strategyName + "_" + evalTf + "_" +
                           token.ToString(CultureInfo.InvariantCulture);
            var jobName = baseName + ".job.json";
            var strategyName2 = baseName + ".json";

            if (File.Exists(Path.Combine(_pendingFolder, jobName)) ||
                File.Exists(Path.Combine(_runningFolder, jobName)))
            {
                skipped.Add(token);
                continue;
            }

            var strategyPath = Path.Combine(_pendingFolder, strategyName2);
            var jobPath = Path.Combine(_pendingFolder, jobName);

            WriteAtomic(strategyPath, ruleJson);
            WriteAtomic(jobPath, BuildJobJson(request, strategyName2, token));
            queued.Add(jobPath);
        }

        return new QueueBatchResult
        {
            QueuedJobPaths = queued,
            SkippedDuplicates = skipped
        };
    }

    private static string BuildJobJson(QueueBatchRequest req, string strategyFile, uint token)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append("\"schemaVersion\":1,");
        sb.Append("\"strategyFile\":\"").Append(Escape(strategyFile)).Append("\",");
        sb.Append("\"instrumentToken\":").Append(token.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"from\":\"").Append(req.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\",");
        sb.Append("\"to\":\"").Append(req.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\",");
        sb.Append("\"mode\":\"").Append(req.Mode).Append("\",");
        sb.Append("\"lossBasis\":\"").Append(req.LossBasis).Append("\",");
        sb.Append("\"tradedCapital\":").Append(req.TradedCapital.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"strategyId\":null,");
        sb.Append("\"tunedId\":null");
        sb.Append('}');
        return sb.ToString();
    }

    private static string SafeTf(Timeframe tf)
    {
        try { return tf.ToString() ?? "D"; } catch { return "D"; }
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
