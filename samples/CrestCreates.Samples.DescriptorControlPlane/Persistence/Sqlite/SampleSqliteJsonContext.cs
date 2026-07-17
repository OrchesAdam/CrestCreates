using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane;

/// <summary>
/// Type-preserving envelope for persisting polymorphic runtime values in SQLite.
/// Each value is stored with its CLR type discriminator so deserialization
/// can reconstruct the exact original type — no reflection, no $type hack, no GUID guessing.
/// </summary>
public sealed record PersistedRuntimeValue
{
    /// <summary>
    /// Assembly-free type discriminator (e.g., "Guid", "CertificationSubmitInput").
    /// Must be one of the types registered in <see cref="RuntimeValueTypeDiscriminator"/>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The serialized payload. Deserialized using the JsonTypeInfo resolved from Type.
    /// </summary>
    public required JsonElement Payload { get; init; }
}

/// <summary>
/// Registry of known runtime value types for SQLite persistence.
/// Maps CLR types to stable string discriminators and back.
/// Only types registered here can be persisted — fail-closed, no reflection fallback.
/// </summary>
public static class RuntimeValueTypeDiscriminator
{
    private static readonly Dictionary<Type, string> TypeToDiscriminator = new()
    {
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(Guid)] = "Guid",
        [typeof(CertificationSubmitInput)] = "CertificationSubmitInput",
        [typeof(CertificationReviewInput)] = "CertificationReviewInput",
        [typeof(CertificationResult)] = "CertificationResult",
        [typeof(Dictionary<string, object?>)] = "DictStrObject",
        [typeof(Dictionary<string, PersistedRuntimeValue>)] = "DictStrPersistedValue",
        [typeof(List<PersistedRuntimeValue>)] = "ListPersistedValue",
    };

    private static readonly Dictionary<string, Type> DiscriminatorToType =
        TypeToDiscriminator.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public static string GetDiscriminator(Type type)
    {
        if (TypeToDiscriminator.TryGetValue(type, out var disc))
            return disc;
        throw new InvalidOperationException(
            $"Type '{type.Name}' is not registered in {nameof(RuntimeValueTypeDiscriminator)}. " +
            "Add it to the registry before persistencing values of this type.");
    }

    public static Type GetType(string discriminator)
    {
        if (DiscriminatorToType.TryGetValue(discriminator, out var type))
            return type;
        throw new InvalidOperationException(
            $"Discriminator '{discriminator}' is not registered in {nameof(RuntimeValueTypeDiscriminator)}. " +
            "The database may contain data from a newer version of the sample.");
    }

    public static bool IsRegistered(Type type) => TypeToDiscriminator.ContainsKey(type);
}

/// <summary>
/// Persistence DTO for WorkflowStepResult with type-preserving Output.
/// The Output field is wrapped in PersistedRuntimeValue for safe serialization.
/// </summary>
public sealed record PersistedWorkflowStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepExecutionStatus Status { get; init; }
    public PersistedRuntimeValue? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Source-generated JSON context for SQLite persistence.
/// All persisted types are explicitly registered — no reflection, no $type discriminator.
/// Reflection is disabled via JsonSerializerIsReflectionEnabledByDefault=false in the project file.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PersistedRuntimeValue))]
[JsonSerializable(typeof(List<PersistedRuntimeValue>))]
[JsonSerializable(typeof(Dictionary<string, PersistedRuntimeValue>))]
[JsonSerializable(typeof(CertificationSubmitInput))]
[JsonSerializable(typeof(CertificationReviewInput))]
[JsonSerializable(typeof(CertificationResult))]
[JsonSerializable(typeof(PersistedWorkflowStepResult))]
[JsonSerializable(typeof(List<PersistedWorkflowStepResult>))]
[JsonSerializable(typeof(List<string>))]
// Primitive types for type-preserving envelope serialization
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(Guid))]
public sealed partial class SampleSqliteJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Wraps a nullable runtime value into a type-preserving envelope for SQLite persistence.
    /// Null values are represented with discriminator "null" so that explicit null keys
    /// in dictionaries survive round-trip (key present but value null vs key absent).
    /// </summary>
    public static PersistedRuntimeValue WrapNullableValue(object? value)
    {
        if (value is null)
        {
            return new PersistedRuntimeValue
            {
                Type = "null",
                Payload = JsonDocument.Parse("null").RootElement.Clone(),
            };
        }

        return WrapValue(value);
    }

    /// <summary>
    /// Wraps a non-null runtime value into a type-preserving envelope for SQLite persistence.
    /// Uses source-generated JsonTypeInfo per type — no reflection.
    /// Dictionary&lt;string, object?&gt; values are recursively wrapped into Dictionary&lt;string, PersistedRuntimeValue&gt;.
    /// </summary>
    public static PersistedRuntimeValue WrapValue(object value)
    {
        var type = value.GetType();
        var discriminator = RuntimeValueTypeDiscriminator.GetDiscriminator(type);
        string json;

        // Special handling: Dictionary<string, object?> must be recursively wrapped
        if (type == typeof(Dictionary<string, object?>))
        {
            var dict = (Dictionary<string, object?>)value;
            var wrapped = new Dictionary<string, PersistedRuntimeValue>(dict.Count);
            foreach (var kvp in dict)
                wrapped[kvp.Key] = WrapNullableValue(kvp.Value);
            json = JsonSerializer.Serialize(wrapped, Default.DictionaryStringPersistedRuntimeValue);
        }
        else
        {
            json = SerializeByDiscriminator(value, discriminator);
        }

        using var document = JsonDocument.Parse(json);
        return new PersistedRuntimeValue { Type = discriminator, Payload = document.RootElement.Clone() };
    }

    /// <summary>
    /// Unwraps a type-preserving envelope back to its original CLR type.
    /// Uses source-generated JsonTypeInfo per type — no reflection.
    /// "null" discriminator returns null. DictStrObject values are recursively unwrapped.
    /// </summary>
    public static object? UnwrapValue(PersistedRuntimeValue? envelope)
    {
        if (envelope is null) return null;

        // Explicit null discriminator — preserves null-valued dictionary keys
        if (envelope.Type == "null")
            return null;

        // Special handling: DictStrObject was stored as DictStrPersistedValue
        if (envelope.Type == "DictStrObject")
        {
            var wrapped = envelope.Payload.Deserialize(Default.DictionaryStringPersistedRuntimeValue);
            if (wrapped is null) return null;
            var result = new Dictionary<string, object?>(wrapped.Count);
            foreach (var kvp in wrapped)
                result[kvp.Key] = UnwrapValue(kvp.Value);
            return result;
        }

        return DeserializeByDiscriminator(envelope.Type, envelope.Payload);
    }

    private static string SerializeByDiscriminator(object value, string discriminator)
    {
        return discriminator switch
        {
            "string" => JsonSerializer.Serialize((string)value, Default.String),
            "bool" => JsonSerializer.Serialize((bool)value, Default.Boolean),
            "int" => JsonSerializer.Serialize((int)value, Default.Int32),
            "long" => JsonSerializer.Serialize((long)value, Default.Int64),
            "double" => JsonSerializer.Serialize((double)value, Default.Double),
            "Guid" => JsonSerializer.Serialize((Guid)value, Default.Guid),
            "CertificationSubmitInput" => JsonSerializer.Serialize((CertificationSubmitInput)value, Default.CertificationSubmitInput),
            "CertificationReviewInput" => JsonSerializer.Serialize((CertificationReviewInput)value, Default.CertificationReviewInput),
            "CertificationResult" => JsonSerializer.Serialize((CertificationResult)value, Default.CertificationResult),
            "DictStrPersistedValue" => JsonSerializer.Serialize((Dictionary<string, PersistedRuntimeValue>)value, Default.DictionaryStringPersistedRuntimeValue),
            "ListPersistedValue" => JsonSerializer.Serialize((List<PersistedRuntimeValue>)value, Default.ListPersistedRuntimeValue),
            _ => throw new InvalidOperationException($"No source-generated serializer for discriminator '{discriminator}'."),
        };
    }

    private static object? DeserializeByDiscriminator(string discriminator, JsonElement payload)
    {
        return discriminator switch
        {
            "string" => payload.Deserialize(Default.String),
            "bool" => payload.Deserialize(Default.Boolean),
            "int" => payload.Deserialize(Default.Int32),
            "long" => payload.Deserialize(Default.Int64),
            "double" => payload.Deserialize(Default.Double),
            "Guid" => payload.Deserialize(Default.Guid),
            "CertificationSubmitInput" => payload.Deserialize(Default.CertificationSubmitInput),
            "CertificationReviewInput" => payload.Deserialize(Default.CertificationReviewInput),
            "CertificationResult" => payload.Deserialize(Default.CertificationResult),
            "DictStrPersistedValue" => payload.Deserialize(Default.DictionaryStringPersistedRuntimeValue),
            "ListPersistedValue" => payload.Deserialize(Default.ListPersistedRuntimeValue),
            _ => throw new InvalidOperationException($"No source-generated deserializer for discriminator '{discriminator}'."),
        };
    }

    /// <summary>
    /// Serializes a Dictionary&lt;string, object?&gt; with type-preserving envelopes.
    /// Explicit null values are preserved with "null" discriminator so that
    /// ContainsKey survives round-trip (key present with null value vs key absent).
    /// </summary>
    public static string? SerializeDictionary(Dictionary<string, object?> dict)
    {
        if (dict.Count == 0) return null;
        var wrapped = new Dictionary<string, PersistedRuntimeValue>(dict.Count);
        foreach (var kvp in dict)
            wrapped[kvp.Key] = WrapNullableValue(kvp.Value);
        return JsonSerializer.Serialize(wrapped, Default.DictionaryStringPersistedRuntimeValue);
    }

    /// <summary>
    /// Deserializes a type-preserving dictionary back to Dictionary&lt;string, object?&gt;.
    /// </summary>
    public static Dictionary<string, object?> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();
        var wrapped = JsonSerializer.Deserialize<Dictionary<string, PersistedRuntimeValue>>(
            json, Default.DictionaryStringPersistedRuntimeValue);
        if (wrapped is null) return new Dictionary<string, object?>();
        var result = new Dictionary<string, object?>(wrapped.Count);
        foreach (var kvp in wrapped)
            result[kvp.Key] = UnwrapValue(kvp.Value);
        return result;
    }

    /// <summary>
    /// Serializes a List&lt;WorkflowStepResult&gt; with type-preserving Output envelopes.
    /// </summary>
    public static string? SerializeStepResults(List<WorkflowStepResult> results)
    {
        if (results.Count == 0) return null;
        var dtos = results.Select(r => new PersistedWorkflowStepResult
        {
            StepId = r.StepId,
            StepName = r.StepName,
            Status = r.Status,
            Output = r.Output is not null ? WrapValue(r.Output) : null,
            ErrorMessage = r.ErrorMessage,
            ExecutedAt = r.ExecutedAt,
            Duration = r.Duration,
        }).ToList();
        return JsonSerializer.Serialize(dtos, Default.ListPersistedWorkflowStepResult);
    }

    /// <summary>
    /// Deserializes type-preserving step results back to List&lt;WorkflowStepResult&gt;.
    /// </summary>
    public static List<WorkflowStepResult> DeserializeStepResults(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<WorkflowStepResult>();
        var dtos = JsonSerializer.Deserialize<List<PersistedWorkflowStepResult>>(
            json, Default.ListPersistedWorkflowStepResult);
        if (dtos is null) return new List<WorkflowStepResult>();
        return dtos.Select(d => new WorkflowStepResult
        {
            StepId = d.StepId,
            StepName = d.StepName,
            Status = d.Status,
            Output = UnwrapValue(d.Output),
            ErrorMessage = d.ErrorMessage,
            ExecutedAt = d.ExecutedAt,
            Duration = d.Duration,
        }).ToList();
    }

    /// <summary>
    /// Serializes an object? field with type-preserving envelope.
    /// </summary>
    public static string? SerializeObjectField(object? value)
    {
        if (value is null) return null;
        var envelope = WrapValue(value);
        return JsonSerializer.Serialize(envelope, Default.PersistedRuntimeValue);
    }

    /// <summary>
    /// Deserializes a type-preserving object? field back to its original CLR type.
    /// </summary>
    public static object? DeserializeObjectField(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var envelope = JsonSerializer.Deserialize<PersistedRuntimeValue>(json, Default.PersistedRuntimeValue);
        return UnwrapValue(envelope);
    }
}
