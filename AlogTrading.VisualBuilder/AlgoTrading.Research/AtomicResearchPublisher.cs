using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlgoTrading.Research.Persistence;

public sealed class AtomicResearchPublisher
{
    private readonly string _runDirectory;
    private readonly Dictionary<string, FileArtifactInfo> _artifacts = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AtomicResearchPublisher(string researchRoot, string researchId, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchRoot);
        _runDirectory = Path.Combine(researchRoot, researchId, runId);
        Directory.CreateDirectory(_runDirectory);
    }

    /// <summary>
    /// Writes payload bytes atomically using a .tmp file, durable flush, and rename.
    /// Tracks artifact size, record count, and computes SHA-256 checksum.
    /// </summary>
    public FileArtifactInfo PublishArtifact(string relativePath, byte[] content, long recordCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        string targetPath = Path.Combine(_runDirectory, relativePath);
        string? parentDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string tempPath = targetPath + ".tmp";

        // 1. Write to temporary file with write-through durability options
        File.WriteAllBytes(tempPath, content);
        using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Flush(true);
        }

        // 2. Compute cryptographic SHA-256 hash and byte size
        string sha256;
        using (var sha = SHA256.Create())
        {
            sha256 = Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant();
        }
        long byteSize = content.Length;

        // 3. Atomic rename (replace if target exists)
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        File.Move(tempPath, targetPath);

        var info = new FileArtifactInfo
        {
            RelativePath = relativePath,
            RecordCount = recordCount,
            ByteSize = byteSize,
            Sha256 = sha256
        };

        _artifacts[relativePath] = info;
        return info;
    }

    /// <summary>
    /// Writes the commit manifest LAST. This acts as the visibility gate 
    /// proving all prerequisite artifacts have been safely committed.
    /// </summary>
    public void Commit(string researchId, string runId)
    {
        var manifest = new ResearchCommitManifest
        {
            SchemaVersion = 1,
            ResearchId = researchId,
            RunId = runId,
            Complete = true,
            CommittedAtUtc = DateTime.UtcNow,
            Artifacts = _artifacts
        };

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        PublishArtifact("commit-manifest.json", manifestBytes, _artifacts.Count);
    }
}