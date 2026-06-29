using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Core.Abstractions.Identity;

public abstract class SemanticStringJsonConverter<TValue> : JsonConverter<TValue>
    where TValue : struct
{
    public sealed override TValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException($"Expected non-null JSON string for {typeof(TValue).Name}.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected JSON string for {typeof(TValue).Name}.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Expected non-empty JSON string for {typeof(TValue).Name}.");
        }

        return Create(value);
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        TValue value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(GetRequiredValue(value));
    }

    protected abstract TValue Create(string value);

    protected abstract string GetRequiredValue(TValue value);
}
