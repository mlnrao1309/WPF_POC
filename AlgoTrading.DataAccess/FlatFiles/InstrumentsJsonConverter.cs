using System.Text.Json;
using System.Text.Json.Serialization;
using Stage1Scanner.Core.Market;

namespace AlgoTrading.DataAccess.FlatFiles;

public class InstrumentsJsonConverter : JsonConverter<Instruments>
{
    public override Instruments Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var token = root.GetProperty("instrumentToken").GetInt64();
        var symbol = root.GetProperty("symbol").GetString() ?? "UNKNOWN";
        
        // 1. Initialize the Core object
        var inst = new Instruments(token, symbol);

        // 2. Restore Candles
        if (root.TryGetProperty("candles", out var candlesArr))
        {
            foreach (var cObj in candlesArr.EnumerateArray())
            {
                var tf = JsonSerializer.Deserialize<Timeframe>(cObj.GetProperty("timeframe").GetRawText(), options);
                var items = JsonSerializer.Deserialize<Candle[]>(cObj.GetProperty("items").GetRawText(), options);
                
                if (items != null)
                    inst.SetCandles(tf, items);
            }
        }

        // 3. Restore Critical Levels (Pivots, S/R)
        if (root.TryGetProperty("levels", out var levelsArr))
        {
            var levels = JsonSerializer.Deserialize<CriticalLevel[]>(levelsArr.GetRawText(), options);
            if (levels != null) 
            {
                foreach (var l in levels) inst.Structures.Add(l);
            }
        }

        // 4. Restore References (Rule Triggers, Trap Days)
        if (root.TryGetProperty("references", out var refsArr))
        {
            var refs = JsonSerializer.Deserialize<Reference[]>(refsArr.GetRawText(), options);
            if (refs != null) 
            {
                foreach (var r in refs) inst.References.Set(r);
            }
        }

        if (root.TryGetProperty("anchors", out var anchorsArr))
        {
            var anchors = JsonSerializer.Deserialize<Anchor[]>(anchorsArr.GetRawText(), options);
            if (anchors != null)
                foreach (var anchor in anchors) inst.Structures.SetAnchor(anchor);
        }
        if (root.TryGetProperty("zones", out var zonesArr))
        {
            var zones = JsonSerializer.Deserialize<CriticalZone[]>(zonesArr.GetRawText(), options);
            if (zones != null)
                foreach (var zone in zones) inst.Structures.Add(zone);
        }

        return inst;
    }

    public override void Write(Utf8JsonWriter writer, Instruments value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("instrumentToken", value.InstrumentToken);
        writer.WriteString("symbol", value.Symbol);

        // 1. Write Candles (Timeframe by Timeframe)
        writer.WritePropertyName("candles");
        writer.WriteStartArray();
        foreach (var tf in value.AvailableTimeframes)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("timeframe");
            JsonSerializer.Serialize(writer, tf, options);
            
            writer.WritePropertyName("items");
            var candles = value.GetCandles(tf);
            JsonSerializer.Serialize(writer, candles, options);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        // 2. Write Critical Levels
        writer.WritePropertyName("levels");
        JsonSerializer.Serialize(writer, value.Structures.Levels.ToArray(), options);

        // 3. Write References
        writer.WritePropertyName("references");
        JsonSerializer.Serialize(writer, value.References.All.ToArray(), options);

        // Anchors and zones are part of the same rule state as levels and references.
        writer.WritePropertyName("anchors");
        JsonSerializer.Serialize(writer, value.Structures.Anchors.ToArray(), options);
        writer.WritePropertyName("zones");
        JsonSerializer.Serialize(writer, value.Structures.Zones.ToArray(), options);
        writer.WriteEndObject();
    }
}