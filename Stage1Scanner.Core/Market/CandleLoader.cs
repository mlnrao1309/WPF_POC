using System.Text.Json;

namespace Stage1Scanner.Core.Market;

/// <summary>
/// Loads candle JSON files for one instrument. Expected filename shape:
/// {token}-{suffix}.json, e.g. 256265-1h.json, 256265-d.json.
/// Weekly candles are NOT loaded from disk; they are aggregated from the
/// daily series.
/// </summary>
public sealed class CandleLoader
{
    public Candles LoadFile(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if (!Path.IsPathRooted(file))
            throw new ArgumentException($"File must be an absolute path. Received: '{file}'.", nameof(file));
        if (!File.Exists(file)) throw new FileNotFoundException("Candle JSON file was not found.", file);
        if (!TryTimeframeFromFileName(file, out var timeframe))
            throw new InvalidDataException($"Cannot determine candle timeframe from '{Path.GetFileName(file)}'.");
        return ParseFile(file, timeframe);
    }

    public Instruments Load(long token, string symbol, string directory)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(directory);

        if (!Path.IsPathRooted(directory))
        {
            throw new ArgumentException(
                $"Directory must be an absolute path. Received: '{directory}'.",
                nameof(directory));
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Candle directory not found: '{directory}'.");
        }

        var instruments = new Instruments(token, symbol);
        var pattern = $"{token}-*.json";

        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            if (!TryTimeframeFromFileName(file, out var timeframe))
            {
                continue;
            }

            if (timeframe.Unit == TimeframeUnit.Week)
            {
                continue;
            }

            var candles = LoadFile(file);
            instruments.SetCandles(candles);
        }

        // Single aggregation site. Guarded against missing or empty daily.
        if (instruments.TryGetCandles(Timeframe.Day, out var daily) &&
            daily.Count > 0)
        {
            instruments.SetCandles(daily.ToWeekly());
        }
        // else: no daily data → Week absent. Downstream rules resolve
        // Unknown/InsufficientData rather than crashing.

        return instruments;
    }

    private static bool TryTimeframeFromFileName(string file, out Timeframe timeframe)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dash = name.LastIndexOf('-');
        if (dash < 0 || dash == name.Length - 1)
        {
            timeframe = default;
            return false;
        }

        var suffix = name[(dash + 1)..].ToLowerInvariant();

        timeframe = suffix switch
        {
            "1m"                  => Timeframe.Minutes(1),
            "3m"                  => Timeframe.Minutes(3),
            "5m"  or "5"          => Timeframe.Minutes(5),
            "15m" or "15"         => Timeframe.Minutes(15),
            "30m" or "30"         => Timeframe.Minutes(30),
            "60m" or "60" or "1h" => Timeframe.Hours(1),
            "2h"                  => Timeframe.Hours(2),
            "4h"                  => Timeframe.Hours(4),
            "d"                   => Timeframe.Day,
            "w"                   => Timeframe.Week,
            "m"                   => Timeframe.Month,
            _                     => default
        };

        return suffix is "1m" or "3m" or "5m" or "5" or "15m" or "15"
                     or "30m" or "30" or "60m" or "60" or "1h"
                     or "2h" or "4h" or "d" or "w" or "m";
    }

    private static Candles ParseFile(string path, Timeframe timeframe)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("candles", out var candles) ||
            candles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Candle JSON at '{path}' has no data.candles array.");
        }

        var list = new List<Candle>(candles.GetArrayLength());

        foreach (var row in candles.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 6)
            {
                continue;
            }

            var openTime = DateTimeOffset.Parse(
                row[0].GetString()
                    ?? throw new InvalidDataException(
                        $"Null timestamp in '{path}'."));

            var open   = row[1].GetDecimal();
            var high   = row[2].GetDecimal();
            var low    = row[3].GetDecimal();
            var close  = row[4].GetDecimal();
            var volume = row[5].GetDecimal();

            decimal? openInterest = null;
            if (row.GetArrayLength() >= 7 &&
                row[6].ValueKind == JsonValueKind.Number)
            {
                openInterest = row[6].GetDecimal();
            }

            list.Add(Candle.Create(
                openTime, timeframe,
                open, high, low, close,
                volume, openInterest));
        }

        return new Candles(timeframe, list);
    }
}
