using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlgoTrading.Data;
using AlgoTrading.Models;

namespace AlgoTrading.Research.Persistence;

public static class DataSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Computes deterministic content digests and snapshotId for the prepared dataset 
    /// and binds them to the exact original strategy bytes without reserialization drift.
    /// </summary>
    public static DataSnapshotManifest CreateAndPublish(
        AtomicResearchPublisher publisher,
        string originalRuleJson,
        uint instrumentToken,
        IReadOnlyDictionary<Timeframe, IBarSeries> prepared,
        DateTime fromUtc,
        DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalRuleJson);
        ArgumentNullException.ThrowIfNull(prepared);

        // 1. Compute SHA-256 of the exact original strategy bytes (UTF-8, no reserialization drift)
        byte[] ruleBytes = Encoding.UTF8.GetBytes(originalRuleJson);
        string originalStrategySha256;
        using (var sha = SHA256.Create())
        {
            originalStrategySha256 = Convert.ToHexString(sha.ComputeHash(ruleBytes)).ToLowerInvariant();
        }

        var timeframeInfos = new Dictionary<string, TimeframeSnapshotInfo>(StringComparer.Ordinal);

        // 2. Compute canonical composite hash across strategy and all prepared timeframes
        using var masterSha = SHA256.Create();
        masterSha.TransformBlock(ruleBytes, 0, ruleBytes.Length, ruleBytes, 0);

        foreach (var kvp in prepared.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            var tf = kvp.Key;
            var series = kvp.Value;

            // Serialize bar series values into a deterministic byte stream for canonical hashing
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            writer.Write(tf.ToString());
            writer.Write(series.Count);

            for (int i = 0; i < series.Count; i++)
            {
                writer.Write(series.DateTime.Span[i].Ticks);
                writer.Write(series.Open.Span[i]);
                writer.Write(series.High.Span[i]);
                writer.Write(series.Low.Span[i]);
                writer.Write(series.Close.Span[i]);
                writer.Write(series.Volume.Span[i]);
                writer.Write(series.OI.Span[i]);

                // Include CloseTime if available from SqlBarSeries subclass
                if (series is SqlBarSeries sqlSeries && sqlSeries.CloseTime.Length > i)
                {
                    writer.Write(sqlSeries.CloseTime.Span[i].Ticks);
                }
            }
            writer.Flush();

            byte[] barDataBytes = ms.ToArray();
            string barSha256;
            using (var barSha = SHA256.Create())
            {
                barSha256 = Convert.ToHexString(barSha.ComputeHash(barDataBytes)).ToLowerInvariant();
            }

            timeframeInfos[tf.ToString()] = new TimeframeSnapshotInfo
            {
                Timeframe = tf.ToString(),
                BarCount = series.Count,
                FromUtc = series.Count > 0 ? series.From : fromUtc,
                ToUtc = series.Count > 0 ? series.To : toUtc,
                BarDataSha256 = barSha256
            };

            // Feed bar data hash bytes into master snapshot hash
            byte[] barShaBytes = Encoding.UTF8.GetBytes(barSha256);
            masterSha.TransformBlock(barShaBytes, 0, barShaBytes.Length, barShaBytes, 0);
        }

        masterSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        // 3. Finalize content-addressed snapshot ID
        string compositeHex = Convert.ToHexString(masterSha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
        string snapshotId = $"snap_{compositeHex[..32]}";

        var manifest = new DataSnapshotManifest
        {
            SchemaVersion = 1,
            SnapshotId = snapshotId,
            OriginalStrategySha256 = originalStrategySha256,
            InstrumentToken = instrumentToken,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Timeframes = timeframeInfos
        };

        // 4. Publish snapshot manifest atomically using our M2-06 publisher framework
        byte[] manifestJsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        publisher.PublishArtifact("datasets/snapshot-manifest.json", manifestJsonBytes, prepared.Count);

        return manifest;
    }
}