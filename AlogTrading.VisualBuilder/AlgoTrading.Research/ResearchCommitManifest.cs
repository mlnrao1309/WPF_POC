using System;
using System.Collections.Generic;

namespace AlgoTrading.Research.Persistence;

public sealed class FileArtifactInfo
{
    public required string RelativePath { get; init; }
    public long RecordCount { get; init; }
    public long ByteSize { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class ResearchCommitManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string ResearchId { get; init; }
    public required string RunId { get; init; }
    public string? SnapshotId { get; init; } // NEW: M2-13 Binding
    public bool Complete { get; init; }
    public DateTime CommittedAtUtc { get; init; }
    public Dictionary<string, FileArtifactInfo> Artifacts { get; init; } = new(StringComparer.Ordinal);
}