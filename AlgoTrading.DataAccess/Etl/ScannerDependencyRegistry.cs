using System.Text.Json;
using System.Globalization;
using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.Etl;

public class RequiredIndicator
{
    public string Timeframe { get; set; } = string.Empty;
    public string IndicatorName { get; set; } = string.Empty; // "EMA"
    public string InputField { get; set; } = string.Empty;    // "Close"
    public int Period { get; set; }                           // 5
    public decimal Factor { get; set; } = 3m;
    public string UniqueKey => $"{IndicatorName}_{Period}_{InputField}_{Factor.ToString("G29", CultureInfo.InvariantCulture)}";
}

public class ScannerDependencyRegistry
{
    private readonly Dictionary<string, RequiredIndicator> _dependencies = new();
    private readonly HashSet<string> _timeframes = new(StringComparer.Ordinal);
    public string Fingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// Reads all scanner JSON files in the directory and extracts unique indicator requirements.
    /// </summary>
    public void LoadFromDirectory(string scannerFolderPath)
    {
        _dependencies.Clear();
        _timeframes.Clear();
        Fingerprint = string.Empty;
        if (!Directory.Exists(scannerFolderPath)) return;
        var fingerprint = new System.Text.StringBuilder();

        var jsonFiles = Directory.GetFiles(scannerFolderPath, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (var file in jsonFiles)
        {
            // Skip snapshot files if they are in the same folder
            if (Path.GetFileName(file).StartsWith("Warmup_") || Path.GetFileName(file).StartsWith("Daily_"))
                continue;

            var json = File.ReadAllText(file);
            fingerprint.Append(Path.GetFileName(file)).Append(':').Append(json.Length).Append(':').Append(json);
            var blueprint = JsonSerializer.Deserialize<ScannerBlueprintDto>(json);

            if (blueprint == null || string.IsNullOrEmpty(blueprint.Timeframe)) continue;

            var tf = WarmupEtlService.ParseTimeframe(blueprint.Timeframe);
            string databaseTimeframe = tf.Unit == Stage1Scanner.Core.Market.TimeframeUnit.Minute
                ? tf.Count.ToString(CultureInfo.InvariantCulture) : tf.ToString();
            _timeframes.Add(databaseTimeframe);
            foreach (var ind in blueprint.Indicators)
            {
                string name = ind.Indicator.Trim().ToUpperInvariant();
                if (name is not ("EMA" or "RSI" or "SUPERTREND"))
                    throw new NotSupportedException($"Unsupported warmup indicator '{ind.Indicator}' in {file}.");
                bool isSuperTrend = name == "SUPERTREND";
                if ((!isSuperTrend && ind.Inputs.Count > 1) || (isSuperTrend && ind.Inputs.Count != 3)
                    || ind.Parameters.Count > (isSuperTrend ? 2 : 1)
                    || ind.Parameters.Count == 0
                    || !int.TryParse(ind.Parameters[0], NumberStyles.None, CultureInfo.InvariantCulture, out int period)
                    || period <= 0)
                    throw new InvalidDataException($"Invalid parameters for {name} in {file}.");
                string[] inputs = ind.Inputs.Select(input => input.Split('.').Last()).ToArray();
                if (isSuperTrend && !inputs.OrderBy(input => input, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(new[] { "Close", "High", "Low" }, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SuperTrend requires High, Low, and Close inputs in {file}.");
                string input = isSuperTrend ? "Close" : inputs.FirstOrDefault() ?? "Close";
                if (!Enum.TryParse<SourceField>(input, true, out var source) || !Enum.IsDefined(source))
                    throw new InvalidDataException($"Unsupported input '{input}' in {file}.");
                decimal factor = 3m;
                if (name == "SUPERTREND" && ind.Parameters.Count == 2
                    && (!decimal.TryParse(ind.Parameters[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out factor) || factor <= 0))
                    throw new InvalidDataException($"Invalid SuperTrend factor in {file}.");
                var req = new RequiredIndicator
                {
                    Timeframe = databaseTimeframe,
                    IndicatorName = name,
                    InputField = source.ToString(),
                    Period = period,
                    Factor = factor
                };

                // Add to dictionary so we don't compute duplicates
                if (!_dependencies.ContainsKey(req.Timeframe + "_" + req.UniqueKey))
                {
                    _dependencies[req.Timeframe + "_" + req.UniqueKey] = req;
                }
            }
        }
        Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString())));
    }

    public IEnumerable<RequiredIndicator> GetIndicatorsForTimeframe(string timeframe)
        => _dependencies.Values.Where(x => x.Timeframe == timeframe);

    public IEnumerable<string> GetActiveTimeframes()
        => _timeframes;
}