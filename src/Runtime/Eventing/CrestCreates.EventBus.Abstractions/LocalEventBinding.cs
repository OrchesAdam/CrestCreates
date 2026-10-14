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
        string replayKey,
        int replayVersion,
        bool replayable,
        JsonTypeInfo? codec,
        ImmutableArray<Type> expectedHandlerTypes,
        ImmutableArray<LocalEventLegacyIdentity> legacyIdentities,
        ImmutableArray<LocalEventReplayAlias> replayAliases,
        ImmutableArray<Type> executionCheckTypes,
        LocalEventPolicyOptions policy,
        string sourceAssembly)
    {
        Invoker = invoker;
        EventType = eventType;
        LocalName = eventType.FullName ?? eventType.Name;
        LocalReplayKey = replayKey;
        ReplayVersion = replayVersion;
        Replayable = replayable;
        Codec = codec;
        ExpectedHandlerTypes = expectedHandlerTypes;
        LegacyIdentities = legacyIdentities;
        ReplayAliases = replayAliases;
        ExecutionCheckTypes = ImmutableArray.CreateRange(executionCheckTypes);
        Governance = policy.Governance;
        TenantMode = policy.TenantMode;
        UseEventValidator = policy.UseEventValidator;
        AllowHistoricalHostContext = policy.AllowHistoricalHostContext;
        AdoptGovernedHistory = policy.AdoptGovernedHistory;
        SourceAssembly = sourceAssembly;
    }

    public LocalEventInvoker Invoker { get; }

    public Type EventType { get; }

    /// <summary>Canonical local name = metadata type full name (never a short name).</summary>
    public string LocalName { get; }

    /// <summary>Frozen replay lookup key: the explicit stable key, or the deterministic default.</summary>
    public string LocalReplayKey { get; }

    public int ReplayVersion { get; }

    public bool Replayable { get; }

    /// <summary>Resolved exactly once at freeze; non-null exactly when <see cref="Replayable"/>.</summary>
    public JsonTypeInfo? Codec { get; }

    /// <summary>Locked read-only copy of the merged required-handler set.</summary>
    public IReadOnlyList<Type> ExpectedHandlerTypes { get; }

    /// <summary>Locked read-only copy of the merged legacy-identity set.</summary>
    public IReadOnlyList<LocalEventLegacyIdentity> LegacyIdentities { get; }

    /// <summary>Locked read-only copy of the merged historical replay-key alias set.</summary>
    public IReadOnlyList<LocalEventReplayAlias> ReplayAliases { get; }

    public LocalEventGovernance Governance { get; }

    public LocalEventTenantMode TenantMode { get; }

    /// <summary>Locked immutable copy of the merged execution-check set; the caller's original collection is never referenced.</summary>
    public IReadOnlyList<Type> ExecutionCheckTypes { get; }

    public bool UseEventValidator { get; }

    public bool AllowHistoricalHostContext { get; }

    public bool AdoptGovernedHistory { get; }

    public string SourceAssembly { get; }

    /// <summary>Structured identity of an execution admitted against this binding.</summary>
    public LocalEventAcceptedIdentity CreateAcceptedIdentity(string? descriptorId, string? descriptorName)
        => new(LocalReplayKey, ReplayVersion, descriptorId, descriptorName);
}
