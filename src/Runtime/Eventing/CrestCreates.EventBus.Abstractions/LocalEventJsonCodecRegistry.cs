using System;
using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Static codec index for replayable local events. Populated by the owning assembly's generated
/// registration (via <c>CrestCreates.Generated.LocalEventJsonCodecContributions</c> emitted by
/// the JsonContracts build-time toolchain); consumed by dispatch registration freeze and the
/// dead-letter replay core. No reflection-based JSON paths are involved.
/// </summary>
public static class LocalEventJsonCodecRegistry
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
