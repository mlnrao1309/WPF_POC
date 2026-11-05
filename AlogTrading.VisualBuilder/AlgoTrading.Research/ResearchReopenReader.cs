using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AlgoTrading.Research.Persistence;

public sealed class ResearchReopenReader
{
    private readonly string _researchRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ResearchReopenReader(string researchRoot)
    {
        _researchRoot = researchRoot ?? throw new ArgumentNullException(nameof(researchRoot));
    }

    /// <summary>
    /// Reconciles orphaned .tmp files during system recovery startup.
    /// </summary>
    public void ReconcileOrphanedTempFiles()
    {
        if (!Directory.Exists(_researchRoot)) return;

        foreach (string tempFile in Directory.GetFiles(_researchRoot, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // Suppress file lock errors during startup sweeps
            }
        }
    }

    /// <summary>
    /// Verifies if a run is fully committed and valid by checking the manifest and file digests.
    /// </summary>
    public bool TryVerifyAndOpenRun(string researchId, string runId, out ResearchCommitManifest? manifest)
    {
        manifest = null;
        string runDir = Path.Combine(_researchRoot, researchId, runId);
        string manifestPath = Path.Combine(runDir, "commit-manifest.json");

        if (!File.Exists(manifestPath))
        {
            return false; // Not committed or incomplete
        }

        try
        {
            string json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ResearchCommitManifest>(json, JsonOptions);

            if (manifest is null || !manifest.Complete || manifest.SchemaVersion != 1)
            {
                return false;
            }

            // Verify every tracked artifact file exists and matches its recorded SHA-256 hash
            foreach (var kvp in manifest.Artifacts)
            {
                string artifactPath = Path.Combine(runDir, kvp.Key);
                var expectedInfo = kvp.Value;

                if (!File.Exists(artifactPath))
                {
                    return false;
                }

                byte[] fileBytes = File.ReadAllBytes(artifactPath); // Standard or File.ReadAllBytes -- could not find ReadAllFileBytesSecure
                if (fileBytes.Length != expectedInfo.ByteSize)
                {
                    return false;
                }

                string actualHash;
                using (var sha = SHA256.Create())
                {
                    actualHash = Convert.ToHexString(sha.ComputeHash(fileBytes)).ToLowerInvariant();
                }

                if (!string.Equals(actualHash, expectedInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        //using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Fallback catch-all for parsing or IO faults
        catch
        {
            manifest = null;
            return false;
        }
    }
}