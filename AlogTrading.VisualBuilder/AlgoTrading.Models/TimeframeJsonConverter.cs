using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models;

public sealed class TimeframeJsonConverter : JsonConverter<Timeframe>
{
    public override Timeframe Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Timeframe must be a string.");
        var s = reader.GetString()!;
        return Timeframe.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Timeframe value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

public sealed class NullableTimeframeJsonConverter : JsonConverter<Timeframe?>
{
    private static readonly TimeframeJsonConverter Inner = new();

    public override Timeframe? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return Inner.Read(ref reader, typeof(Timeframe), options);
    }

    public override void Write(Utf8JsonWriter writer, Timeframe? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        Inner.Write(writer, value.Value, options);
    }
}