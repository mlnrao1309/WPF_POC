using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Screener.Storage;

public sealed class FileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSettingsStore()
    {
        ScreenerPaths.EnsureUserDataFolder();
    }

    public async Task<WindowSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(ScreenerPaths.SettingsFile))
                return WindowSettings.Empty;

            try
            {
                await using var stream = File.OpenRead(ScreenerPaths.SettingsFile);
                var settings = await JsonSerializer
                    .DeserializeAsync<WindowSettings>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);

                return settings ?? WindowSettings.Empty;
            }
            catch (JsonException)
            {
                // Corrupted file — treat as empty rather than crashing.
                return WindowSettings.Empty;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WindowSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScreenerPaths.SettingsFile)!);

            string tmp = ScreenerPaths.SettingsFile + ".tmp";

            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, JsonOptions, ct)
                    .ConfigureAwait(false);
            }

            File.Move(tmp, ScreenerPaths.SettingsFile, overwrite: true);
        }
        finally { _gate.Release(); }
    }
}