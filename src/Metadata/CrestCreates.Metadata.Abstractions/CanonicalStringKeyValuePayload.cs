using System.Text.Json.Serialization;

namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Canonical key-value payload for dictionary canonicalization.
/// Dictionaries must not appear directly in canonical payloads — they are converted
/// to ordered key-value lists via <see cref="CanonicalHashCollectionOrderMode.OrderedKeyValue"/>.
/// </summary>
public sealed record CanonicalStringKeyValuePayload
{
    [JsonPropertyOrder(0)]
    public required string Key { get; init; }

    [JsonPropertyOrder(1)]
    public string? Value { get; init; }
}

/// <summary>
/// Generic canonical key-value payload for typed dictionary values.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed record CanonicalStringKeyValuePayload<TValue>
{
    [JsonPropertyOrder(0)]
    public required string Key { get; init; }

    [JsonPropertyOrder(1)]
    public required TValue? Value { get; init; }
}
