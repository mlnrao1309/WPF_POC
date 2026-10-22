using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.DataAccess.FlatFiles;

public static class FlatFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        // Register our custom converter for the Core Instruments object
        options.Converters.Add(new InstrumentsJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter()); // For readability (e.g. "Minute" instead of 1)
        return options;
    }

    public static async Task SaveSnapshotAsync(string filePath, SnapshotFile snapshot, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string tempPath = filePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct);
        }

        // Atomic swap
        File.Move(tempPath, filePath, overwrite: true);
    }

    public static async Task<SnapshotFile?> LoadSnapshotAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<SnapshotFile>(stream, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Corrupt snapshot file {filePath}: {ex.Message}");
            return null;
        }
    }
}