using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.HealthCheck.AspNetCore.Serialization;

/// <summary>
/// Strongly-typed container for health check result data (key-value pairs with mixed primitive types).
/// Uses a custom converter (<see cref="HealthReportDataConverter"/>) to handle
/// AOT-safe serialization without reflection.
/// </summary>
[JsonConverter(typeof(HealthReportDataConverter))]
public sealed class HealthReportData
{
    private readonly Dictionary<string, object?> _entries = new();

    public IReadOnlyDictionary<string, object?> Entries => _entries;

    public HealthReportData() { }

    public HealthReportData(IReadOnlyDictionary<string, object> source)
    {
        foreach (var kvp in source)
            _entries[kvp.Key] = kvp.Value;
    }

    public int Count => _entries.Count;
}

/// <summary>
/// AOT-safe JSON converter for <see cref="HealthReportData"/>.
/// Handles primitive values, nested dictionaries, and lists recursively.
/// </summary>
internal sealed class HealthReportDataConverter : JsonConverter<HealthReportData>
{
    public override HealthReportData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Deserialization is not supported.");

    public override void Write(Utf8JsonWriter writer, HealthReportData value, JsonSerializerOptions options)
        => WriteDictionary(writer, value.Entries);

    private static void WriteDictionary(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> dict)
    {
        writer.WriteStartObject();
        foreach (var kvp in dict)
            WriteValue(writer, kvp.Key, kvp.Value);
        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, IEnumerable<object?> list)
    {
        writer.WriteStartArray();
        foreach (var item in list)
            WriteValue(writer, null, item);
        writer.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, string? key, object? value)
    {
        switch (value)
        {
            case int i:
                if (key != null) writer.WriteNumber(key, i);
                else writer.WriteNumberValue(i);
                break;
            case long l:
                if (key != null) writer.WriteNumber(key, l);
                else writer.WriteNumberValue(l);
                break;
            case double d:
                if (key != null) writer.WriteNumber(key, d);
                else writer.WriteNumberValue(d);
                break;
            case float f:
                if (key != null) writer.WriteNumber(key, f);
                else writer.WriteNumberValue(f);
                break;
            case bool b:
                if (key != null) writer.WriteBoolean(key, b);
                else writer.WriteBooleanValue(b);
                break;
            case null:
                if (key != null) writer.WriteNull(key);
                else writer.WriteNullValue();
                break;
            case string s:
                if (key != null) writer.WriteString(key, s);
                else writer.WriteStringValue(s);
                break;
            case IReadOnlyDictionary<string, object?> nestedDict:
                if (key != null) { writer.WriteStartObject(key); }
                else { writer.WriteStartObject(); }
                foreach (var kvp in nestedDict)
                    WriteValue(writer, kvp.Key, kvp.Value);
                writer.WriteEndObject();
                break;
            case List<object> list:
                if (key != null) writer.WriteStartArray(key);
                else writer.WriteStartArray();
                foreach (var item in list)
                    WriteValue(writer, null, item);
                writer.WriteEndArray();
                break;
            case IList<object> list:
                if (key != null) writer.WriteStartArray(key);
                else writer.WriteStartArray();
                foreach (var item in list)
                    WriteValue(writer, null, item);
                writer.WriteEndArray();
                break;
            default:
                if (key != null) writer.WriteString(key, value?.ToString());
                else writer.WriteStringValue(value?.ToString());
                break;
        }
    }
}
