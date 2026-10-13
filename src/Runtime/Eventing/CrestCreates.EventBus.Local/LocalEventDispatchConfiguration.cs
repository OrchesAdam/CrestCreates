using System;
using System.Collections.Generic;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Accumulates every manual declaration callback registered against the same service collection.
/// The native registry factory resolves the contributor set once, but the configuration callbacks
/// are held here so a later <c>AddCrestLocalEventDispatch(builder =&gt; ...)</c> call after an
/// earlier parameterless call is not silently dropped by <c>TryAddSingleton</c>.
/// </summary>
internal sealed class LocalEventDispatchConfiguration
{
    private readonly List<Action<LocalEventDispatchRegistrationBuilder>> _callbacks = new();

    public IReadOnlyList<Action<LocalEventDispatchRegistrationBuilder>> Callbacks => _callbacks;

    public void Add(Action<LocalEventDispatchRegistrationBuilder> configure) => _callbacks.Add(configure);
}
