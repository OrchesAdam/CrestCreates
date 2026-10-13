using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Event.Abstractions;

/// <summary>
/// Static codec index for wire-published events ([CrestEvent] with Integration scope). Populated by
/// the owning assembly's generated registration (via the JsonContracts build-time toolchain); consumed
/// by transport binding freeze to snapshot exact <see cref="JsonTypeInfo"/> per binding. No
/// reflection-based JSON paths are involved.
/// </summary>
public static class EventJsonCodecRegistry
{
    private static readonly ConcurrentDictionary<Type, Func<JsonTypeInfo?>> Factories = new();

    /// <summary>Registers a source-generated codec factory. Duplicate registration keeps the first.</summary>
    public static void Register(Type eventType, Func<JsonTypeInfo?> codecFactory)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(codecFactory);
        Factories.TryAdd(eventType, codecFactory);
    }

    public static JsonTypeInfo? Resolve(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return Factories.TryGetValue(eventType, out var factory) ? factory() : null;
    }
}
