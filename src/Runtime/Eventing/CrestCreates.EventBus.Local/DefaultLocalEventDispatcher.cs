using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Unified typed dispatcher. Both entry points resolve the exact runtime event type in the
/// frozen dispatch registry and invoke the compile-time closed invoker; handlers are resolved
/// from the provider of the caller's scope. Undeclared events fail deterministically before any
/// handler runs — there is no reflection path and no fallback.
/// </summary>
public class DefaultLocalEventDispatcher : ILocalEventDispatcher
{
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceProvider _serviceProvider;

    public DefaultLocalEventDispatcher(
        ILocalEventDispatchRegistry registry,
        IServiceProvider serviceProvider)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
    }

    public Task DispatchAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(@event.GetType(), out var binding))
        {
            throw new UnknownLocalEventException(@event.GetType());
        }

        // Validate against the binding's fully merged expected-handler list — not the retained
        // invoker's contributor-local closure — so a required handler contributed by any assembly is
        // enforced before the first handler runs.
        if (binding.ExpectedHandlerTypes.Count > 0)
        {
            var instances = binding.Invoker.ProbeHandlerInstances(_serviceProvider);
            LocalEventDispatch.ValidateRequiredHandlers(binding.EventType, binding.ExpectedHandlerTypes, instances);
        }

        var pending = binding.Invoker.InvokeAsync(_serviceProvider, @event, cancellationToken);
        return pending.IsCompletedSuccessfully ? Task.CompletedTask : pending.AsTask();
    }

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        return DispatchAsync((ILocalEvent)@event, cancellationToken);
    }
}
