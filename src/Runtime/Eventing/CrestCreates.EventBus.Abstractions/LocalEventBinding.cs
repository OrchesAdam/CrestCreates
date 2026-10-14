using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// A frozen execution entry of the local event dispatch index. Bindings hold fixed metadata, the
/// closed typed preparer, locked read-only collections and the codec resolved once at freeze —
/// never factories, providers, scopes or handler instances.
/// </summary>
public sealed class LocalEventBinding
{
    internal LocalEventBinding(
        LocalEventInvoker invoker,
        Type eventType,
        int replayVersion,
        bool replayable,
        JsonTypeInfo? codec,
        ImmutableArray<Type> expectedHandlerTypes,
        ImmutableArray<LocalEventLegacyIdentity> legacyIdentities,
        string sourceAssembly)
    {
        Invoker = invoker;
        EventType = eventType;
        LocalName = eventType.FullName ?? eventType.Name;
        LocalReplayKey = LocalEventIdentity.BuildLocalReplayKey(eventType);
        ReplayVersion = replayVersion;
        Replayable = replayable;
        Codec = codec;
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

    /// <summary>Resolved exactly once at freeze; non-null exactly when <see cref="Replayable"/>.</summary>
    public JsonTypeInfo? Codec { get; }

    /// <summary>Locked read-only copy of the merged required-handler set.</summary>
    public IReadOnlyList<Type> ExpectedHandlerTypes { get; }

    /// <summary>Locked read-only copy of the merged legacy-identity set.</summary>
    public IReadOnlyList<LocalEventLegacyIdentity> LegacyIdentities { get; }

    public string SourceAssembly { get; }
}
