using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Shared registration surface for generated contributors and explicit manual assembly.
/// Both paths write declarations into the same builder and are subject to the same conflict
/// validation at freeze time — this is not a second dispatcher. Registrations carry assembly-time
/// codec inputs and metadata only; freeze produces the frozen bindings.
/// </summary>
public sealed class LocalEventDispatchRegistrationBuilder
{
    private readonly List<LocalEventRegistration> _registrations = new();

    public IReadOnlyList<LocalEventRegistration> Registrations => _registrations;

    public void Add(LocalEventRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations.Add(registration);
    }

    /// <summary>
    /// Declares an event without a generated contributor. Handlers registered through DI are
    /// dispatched in DI registration order; typed expected-handler diagnostics require a
    /// generated contributor. An optional manual typed codec factory participates in the same
    /// freeze-time validation as generated per-host codec contributions. Policy inputs (stable
    /// key, aliases, governance/tenant mode, explicit checks) join the same freeze validation.
    /// </summary>
    public LocalEventRegistration Declare<TEvent>(
        bool replayable = false,
        int version = 1,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities = null,
        Func<JsonTypeInfo?>? codecFactory = null,
        LocalEventPolicyOptions? policy = null)
        where TEvent : ILocalEvent
    {
        var registration = LocalEventRegistration.Create(
            new LocalEventInvoker<TEvent>(),
            typeof(TEvent),
            version,
            replayable,
            codecFactory,
            expectedHandlerTypes: null,
            legacyIdentities,
            SourceAssemblyOf(typeof(TEvent)),
            policy);
        Add(registration);
        return registration;
    }

    /// <summary>Declares a third-party event type for membership/codec capability only.</summary>
    public LocalEventRegistration Declare(
        Type eventType,
        bool replayable = false,
        int version = 1,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities = null,
        Func<JsonTypeInfo?>? codecFactory = null,
        LocalEventPolicyOptions? policy = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        if (!typeof(ILocalEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException(
                $"Type '{eventType.FullName}' does not implement {nameof(ILocalEvent)}.", nameof(eventType));
        }

        var registration = LocalEventRegistration.Create(
            InertLocalEventInvoker.Instance,
            eventType,
            version,
            replayable,
            codecFactory,
            expectedHandlerTypes: null,
            legacyIdentities,
            SourceAssemblyOf(eventType),
            policy);
        Add(registration);
        return registration;
    }

    private static string SourceAssemblyOf(Type type) => type.Assembly.GetName().Name ?? string.Empty;
}
