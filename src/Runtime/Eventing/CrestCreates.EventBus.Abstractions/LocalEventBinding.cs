using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// A frozen registration entry of the local event dispatch index. Bindings hold static
/// metadata, typed delegates and source-generated JSON type info only — never service
/// providers, scopes or handler instances.
/// </summary>
public sealed class LocalEventBinding
{
    private LocalEventBinding(
        LocalEventInvoker invoker,
        Type eventType,
        string localName,
        string localReplayKey,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type> expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity> legacyIdentities,
        string sourceAssembly)
    {
        Invoker = invoker;
        EventType = eventType;
        LocalName = localName;
        LocalReplayKey = localReplayKey;
        ReplayVersion = replayVersion;
        Replayable = replayable;
        CodecFactory = codecFactory;
        ExpectedHandlerTypes = expectedHandlerTypes;
        LegacyIdentities = legacyIdentities;
        SourceAssembly = sourceAssembly;
    }

    public LocalEventInvoker Invoker { get; }

    public Type EventType { get; }

    /// <summary>Canonical local name = metadata type full name (never a short name).</summary>
    public string LocalName { get; }

    /// <summary>Compile-time deterministic replay lookup key: "{assemblySimpleName}:{metadataFullName}".</summary>
    public string LocalReplayKey { get; }

    public int ReplayVersion { get; }

    public bool Replayable { get; }

    /// <summary>Non-null exactly when <see cref="Replayable"/>; resolved and validated at freeze.</summary>
    public Func<JsonTypeInfo?>? CodecFactory { get; }

    public IReadOnlyList<Type> ExpectedHandlerTypes { get; }

    public IReadOnlyList<LocalEventLegacyIdentity> LegacyIdentities { get; }

    public string SourceAssembly { get; }

    /// <summary>Creates a typed binding for a compile-time closed event type.</summary>
    public static LocalEventBinding Create<TEvent>(
        LocalEventInvoker<TEvent> invoker,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities,
        string sourceAssembly)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(invoker);

        var eventType = typeof(TEvent);
        return new LocalEventBinding(
            invoker,
            eventType,
            eventType.FullName ?? eventType.Name,
            LocalEventIdentity.BuildLocalReplayKey(eventType),
            replayVersion,
            replayable,
            codecFactory,
            expectedHandlerTypes ?? Array.Empty<Type>(),
            legacyIdentities ?? Array.Empty<LocalEventLegacyIdentity>(),
            sourceAssembly);
    }

    /// <summary>Creates a non-generic binding (host-side typeof declarations and registry merge).</summary>
    public static LocalEventBinding Create(
        LocalEventInvoker invoker,
        Type eventType,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities,
        string sourceAssembly)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(eventType);

        return new LocalEventBinding(
            invoker,
            eventType,
            eventType.FullName ?? eventType.Name,
            LocalEventIdentity.BuildLocalReplayKey(eventType),
            replayVersion,
            replayable,
            codecFactory,
            expectedHandlerTypes ?? Array.Empty<Type>(),
            legacyIdentities ?? Array.Empty<LocalEventLegacyIdentity>(),
            sourceAssembly);
    }
}
