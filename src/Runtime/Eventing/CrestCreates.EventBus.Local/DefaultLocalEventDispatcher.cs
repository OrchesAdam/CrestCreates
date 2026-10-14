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

    public async Task DispatchAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_registry.TryGet(@event.GetType(), out var binding))
        {
            throw new UnknownLocalEventException(@event.GetType());
        }

        // Direct dispatch borrows the caller's scope: the shared admission core (governance,
        // explicit checks, activated validator) runs in this frame, then one Prepare resolves the
        // whole handler batch and the same instances execute.
        var snapshot = LocalEventContextBridge.Capture(_serviceProvider);
        var admission = await LocalEventAdmission
            .AdmitAsync(_serviceProvider, binding, @event, LocalEventExecutionSource.DirectDispatch, snapshot,
                persistedDescriptorId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Allowed)
        {
            throw new LocalEventAdmissionException(admission.ProblemCode!, admission.Problem!);
        }

        await LocalEventExecution.ExecuteAsync(_serviceProvider, binding, @event, cancellationToken).ConfigureAwait(false);
    }

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        return DispatchAsync((ILocalEvent)@event, cancellationToken);
    }
}
