using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Screener.Storage;

/// <summary>
/// File-backed query store. Uses a single JSON file with atomic writes
/// (write to .tmp, then File.Move with overwrite) so a crash mid-write
/// can never leave a half-written file.
///
/// A SemaphoreSlim serializes access because two UI actions (Save and
/// Delete) could race otherwise.
/// </summary>
public sealed class FileQueryStore : IQueryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileQueryStore()
    {
        ScreenerPaths.EnsureUserDataFolder();
    }

    // ---------- public API ----------

    public async Task<IReadOnlyList<SavedQuery>> GetSavedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadFileAsync(ScreenerPaths.SavedQueriesFile, ct).ConfigureAwait(false);

            return all
                .Where(q => !string.Equals(q.Category, "Template", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.LastRunUtc ?? q.CreatedUtc)
                .ToList();
        }
        finally { _gate.Release(); }
    }
    internal sealed record QueryFile(
    int Version,
    List<SavedQuery> Queries);
    public async Task<IReadOnlyList<SavedQuery>> GetTemplatesAsync(CancellationToken ct = default)
    {
        // Templates are read-only. No lock needed.
        var all = await ReadFileAsync(ScreenerPaths.TemplatesFile, ct).ConfigureAwait(false);

        return all
            .Where(q => string.Equals(q.Category, "Template", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task UpsertAsync(SavedQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Id))
            throw new ArgumentException("Query must have an Id.", nameof(query));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadFileAsync(ScreenerPaths.SavedQueriesFile, ct).ConfigureAwait(false);

            int idx = all.FindIndex(q => q.Id == query.Id);
            if (idx >= 0) all[idx] = query;
            else          all.Add(query);

            await WriteFileAsync(ScreenerPaths.SavedQueriesFile, all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadFileAsync(ScreenerPaths.SavedQueriesFile, ct).ConfigureAwait(false);
            all.RemoveAll(q => q.Id == id);
            await WriteFileAsync(ScreenerPaths.SavedQueriesFile, all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task RecordRunAsync(string id, int rowCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadFileAsync(ScreenerPaths.SavedQueriesFile, ct).ConfigureAwait(false);

            int idx = all.FindIndex(q => q.Id == id);
            if (idx < 0) return;

            all[idx] = all[idx] with
            {
                LastRunUtc      = DateTime.UtcNow,
                LastRunRowCount = rowCount
            };

            await WriteFileAsync(ScreenerPaths.SavedQueriesFile, all, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ---------- internals ----------

    private static async Task<List<SavedQuery>> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new List<SavedQuery>();

        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer
                .DeserializeAsync<QueryFile>(stream, JsonOptions, ct)
                .ConfigureAwait(false);

            return file?.Queries ?? new List<SavedQuery>();
        }
        catch (JsonException)
        {
            // Corrupted file — treat as empty rather than crashing the app.
            // The original file is left on disk so a user can inspect it.
            return new List<SavedQuery>();
        }
    }

    /// <summary>
    /// Write to a temp file, then atomically replace the target.
    /// If the process dies mid-write, the target file is untouched.
    /// </summary>
    private static async Task WriteFileAsync(
        string path, List<SavedQuery> entries, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string tmp = path + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            //await JsonSerializer
            //    .SerializeAsync(stream, entries, JsonOptions, ct)
            //    .ConfigureAwait(false);
            var wrapper = new QueryFile(Version: 1, Queries: entries);

            await JsonSerializer
                .SerializeAsync(stream, wrapper, JsonOptions, ct);
        }

        // File.Move with overwrite is atomic on NTFS for same-volume moves.
        File.Move(tmp, path, overwrite: true);
    }
}