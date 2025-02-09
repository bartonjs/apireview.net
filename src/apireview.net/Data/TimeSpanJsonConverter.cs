using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiReviewDotNet.Data;

public class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long ticks = reader.GetInt64();
        return new TimeSpan(ticks);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Ticks);
    }
}
