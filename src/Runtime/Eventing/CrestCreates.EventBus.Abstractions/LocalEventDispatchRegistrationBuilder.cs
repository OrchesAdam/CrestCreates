using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Shared registration surface for generated contributors and explicit manual assembly.
/// Both paths write into the same builder and are subject to the same conflict validation at
/// freeze time — this is not a second dispatcher.
/// </summary>
public sealed class LocalEventDispatchRegistrationBuilder
{
    private readonly List<LocalEventBinding> _bindings = new();

    public IReadOnlyList<LocalEventBinding> Bindings => _bindings;

    public void Add(LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _bindings.Add(binding);
    }

    /// <summary>
    /// Declares an event without a generated contributor. Handlers registered through DI are
    /// dispatched in DI registration order; typed expected-handler diagnostics require a
    /// generated contributor.
    /// </summary>
    public LocalEventBinding Declare<TEvent>(
        bool replayable = false,
        int version = 1,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities = null)
        where TEvent : ILocalEvent
    {
        var binding = LocalEventBinding.Create(
            new LocalEventInvoker<TEvent>(static (provider, @event, cancellationToken) =>
                LocalEventDispatch.InvokeHandlersAsync(provider, @event, cancellationToken)),
            typeof(TEvent),
            version,
            replayable,
            replayable ? static () => LocalEventJsonCodecRegistry.Resolve(typeof(TEvent)) : null,
            expectedHandlerTypes: null,
            legacyIdentities,
            SourceAssemblyOf(typeof(TEvent)));
        Add(binding);
        return binding;
    }

    /// <summary>Declares a third-party event type for membership/codec capability only.</summary>
    public LocalEventBinding Declare(
        Type eventType,
        bool replayable = false,
        int version = 1,
        IReadOnlyList<LocalEventLegacyIdentity>? legacyIdentities = null)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        if (!typeof(ILocalEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException(
                $"Type '{eventType.FullName}' does not implement {nameof(ILocalEvent)}.", nameof(eventType));
        }

        var binding = LocalEventBinding.Create(
            InertLocalEventInvoker.Instance,
            eventType,
            version,
            replayable,
            replayable ? CreateCodecFactory(eventType) : null,
            expectedHandlerTypes: null,
            legacyIdentities,
            SourceAssemblyOf(eventType));
        Add(binding);
        return binding;
    }

    internal static Func<JsonTypeInfo?> CreateCodecFactory(Type eventType)
        => () => LocalEventJsonCodecRegistry.Resolve(eventType);

    private static string SourceAssemblyOf(Type type) => type.Assembly.GetName().Name ?? string.Empty;
}
