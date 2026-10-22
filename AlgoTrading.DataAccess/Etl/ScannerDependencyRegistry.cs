using System.Text.Json;

namespace AlgoTrading.DataAccess.Etl;

public class RequiredIndicator
{
    public string Timeframe { get; set; } = string.Empty;
    public string IndicatorName { get; set; } = string.Empty; // "EMA"
    public string InputField { get; set; } = string.Empty;    // "Close"
    public int Period { get; set; }                           // 5
    public string UniqueKey => $"{IndicatorName}_{InputField}_{Period}_{Timeframe}";
}

public class ScannerDependencyRegistry
{
    private readonly Dictionary<string, RequiredIndicator> _dependencies = new();

    /// <summary>
    /// Reads all scanner JSON files in the directory and extracts unique indicator requirements.
    /// </summary>
    public void LoadFromDirectory(string scannerFolderPath)
    {
        if (!Directory.Exists(scannerFolderPath)) return;

        var jsonFiles = Directory.GetFiles(scannerFolderPath, "*.json");
        
        foreach (var file in jsonFiles)
        {
            // Skip snapshot files if they are in the same folder
            if (Path.GetFileName(file).StartsWith("Warmup_") || Path.GetFileName(file).StartsWith("Daily_"))
                continue;

            var json = File.ReadAllText(file);
            var blueprint = JsonSerializer.Deserialize<ScannerBlueprintDto>(json);
            
            if (blueprint == null || string.IsNullOrEmpty(blueprint.Timeframe)) continue;

            foreach (var ind in blueprint.Indicators)
            {
                // Basic parsing for EMA / RSI types
                if (ind.Parameters.Count > 0 && int.TryParse(ind.Parameters[0], out int period))
                {
                    var req = new RequiredIndicator
                    {
                        Timeframe = blueprint.Timeframe,
                        IndicatorName = ind.Indicator.ToUpper(),
                        InputField = ind.Inputs.FirstOrDefault() ?? "Close",
                        Period = period
                    };

                    // Add to dictionary so we don't compute duplicates
                    if (!_dependencies.ContainsKey(req.UniqueKey))
                    {
                        _dependencies[req.UniqueKey] = req;
                    }
                }
            }
        }
    }

    public IEnumerable<RequiredIndicator> GetIndicatorsForTimeframe(string timeframe)
        => _dependencies.Values.Where(x => x.Timeframe == timeframe);
        
    public IEnumerable<string> GetActiveTimeframes()
        => _dependencies.Values.Select(x => x.Timeframe).Distinct();
}