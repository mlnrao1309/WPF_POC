using AlgoTrading.DataAccess.FlatFiles;

namespace AlgoTrading.DataAccess.Runtime;

public class SnapshotLoader
{
    private readonly string _resourcesFolder;

    public SnapshotLoader(string resourcesFolder)
    {
        _resourcesFolder = resourcesFolder;
    }

    public async Task<RuntimeSnapshot> LoadLatestSnapshotAsync()
    {
        var snapshot = new RuntimeSnapshot { SnapshotDate = DateTime.Today };

        // 1. Find the latest Equities and FO files
        var equityFile = GetLatestFile("Warmup_Equities_*.json");
        var foFile = GetLatestFile("Warmup_FO_*.json");

        // 2. Load Equities
        if (equityFile != null)
        {
            var eqDto = await FlatFileManager.LoadSnapshotAsync(equityFile);
            if (eqDto != null)
            {
                foreach (var kvp in eqDto.Instruments) snapshot.Instruments[kvp.Key] = kvp.Value;
                foreach (var kvp in eqDto.IndicatorSeries) snapshot.IndicatorSeries[kvp.Key] = kvp.Value;
            }
        }

        // 3. Load F&O
        if (foFile != null)
        {
            var foDto = await FlatFileManager.LoadSnapshotAsync(foFile);
            if (foDto != null)
            {
                foreach (var kvp in foDto.Instruments) snapshot.Instruments[kvp.Key] = kvp.Value;
                foreach (var kvp in foDto.IndicatorSeries) snapshot.IndicatorSeries[kvp.Key] = kvp.Value;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[Loader] Loaded {snapshot.Instruments.Count} instruments from flat files.");
        return snapshot;
    }

    private string? GetLatestFile(string searchPattern)
    {
        if (!Directory.Exists(_resourcesFolder)) return null;
        
        var dir = new DirectoryInfo(_resourcesFolder);
        var files = dir.GetFiles(searchPattern).OrderByDescending(f => f.LastWriteTime).ToList();
        
        return files.FirstOrDefault()?.FullName;
    }
}