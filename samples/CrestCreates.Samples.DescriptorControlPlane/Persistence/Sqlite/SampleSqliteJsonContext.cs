using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// JSON serialization configuration for SQLite persistence in the Company Certification sample.
/// All serialization uses ReflectionOptions since Dictionary&lt;string, object?&gt; and object?
/// fields contain polymorphic workflow variables that cannot be represented in source-generated contexts.
/// This is acceptable for a sample project — not framework core.
/// </summary>
public static class SampleSqliteJsonContext
{
    /// <summary>
    /// Reflection-based options for serializing Dictionary&lt;string, object?&gt;
    /// and object? fields. This is acceptable for a sample project — not framework core.
    /// Workflow variables and HumanTask input/output are inherently polymorphic
    /// and cannot be represented in source-generated contexts.
    /// </summary>
    public static readonly JsonSerializerOptions ReflectionOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Converts a deserialized value (possibly JsonElement) back to its CLR type.
    /// Handles nested objects and arrays recursively.
    /// Guid strings are detected via TryGetGuid for workflow variable compatibility.
    /// </summary>
    public static object? ConvertJsonElement(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.TryGetGuid(out var g) ? g : element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Null => null,
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(
                    element.GetRawText(), ReflectionOptions)
                ?.Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    element.GetRawText(), ReflectionOptions)
                ?.ToDictionary(kvp => kvp.Key, kvp => ConvertJsonElement(kvp.Value)),
            _ => element.ToString()
        };
    }
}
