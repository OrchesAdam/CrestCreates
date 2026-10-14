using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Builder-level declaration of one local event. It carries the assembly-time codec factory input (if
/// any) and all single-value metadata; it never enters the runtime index. Freeze resolves factories,
/// copies the metadata collections and produces a frozen <see cref="LocalEventBinding"/>.
/// </summary>
public sealed class LocalEventRegistration
{
    private LocalEventRegistration(
        LocalEventInvoker invoker,
        Type eventType,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type> expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity> legacyIdentities,
        string sourceAssembly,
        LocalEventPolicyOptions policy)
    {
        Invoker = invoker;
        EventType = eventType;
        ReplayVersion = replayVersion;
        Replayable = replayable;
        CodecFactory = codecFactory;
        ExpectedHandlerTypes = expectedHandlerTypes;
        LegacyIdentities = legacyIdentities;
        SourceAssembly = sourceAssembly;
        Policy = policy;
    }

    public LocalEventInvoker Invoker { get; }

    public Type EventType { get; }

    public int ReplayVersion { get; }

    public bool Replayable { get; }

    /// <summary>Stable key, aliases, governance/tenant policy and explicit checks of this declaration.</summary>
    public LocalEventPolicyOptions Policy { get; }

    /// <summary>Effective replay key of this declaration (explicit stable key or the deterministic default).</summary>
    public string EffectiveReplayKey => Policy.ReplayKey ?? LocalEventIdentity.BuildLocalReplayKey(EventType);

    /// <summary>
    /// Assembly-time codec input (manual typed codec); resolved exactly once at freeze and never
    /// stored on the frozen binding. Generated contributors leave this null because their codec
    /// arrives through the owning assembly's per-host codec contribution.
    /// </summary>
    public Func<JsonTypeInfo?>? CodecFactory { get; }

    public IReadOnlyList<Type> ExpectedHandlerTypes { get; }

    public IReadOnlyList<LocalEventLegacyIdentity> LegacyIdentities { get; }

    public string SourceAssembly { get; }

    /// <summary>Creates a typed declaration for a compile-time closed event type.</summary>
    public static LocalEventRegistration Create<TEvent>(
        LocalEventInvoker<TEvent> invoker,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities,
        string sourceAssembly,
        LocalEventPolicyOptions? policy = null)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(invoker);

        return new LocalEventRegistration(
            invoker,
            typeof(TEvent),
            replayVersion,
            replayable,
            codecFactory,
            expectedHandlerTypes ?? Array.Empty<Type>(),
            legacyIdentities ?? Array.Empty<LocalEventLegacyIdentity>(),
            sourceAssembly,
            policy ?? LocalEventPolicyOptions.Default);
    }

    /// <summary>Creates a non-generic declaration (host-side typeof declarations and registry merge).</summary>
    public static LocalEventRegistration Create(
        LocalEventInvoker invoker,
        Type eventType,
        int replayVersion,
        bool replayable,
        Func<JsonTypeInfo?>? codecFactory,
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities,
        string sourceAssembly,
        LocalEventPolicyOptions? policy = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(eventType);

        return new LocalEventRegistration(
            invoker,
            eventType,
            replayVersion,
            replayable,
            codecFactory,
            expectedHandlerTypes ?? Array.Empty<Type>(),
            legacyIdentities ?? Array.Empty<LocalEventLegacyIdentity>(),
            sourceAssembly,
            policy ?? LocalEventPolicyOptions.Default);
    }
}
