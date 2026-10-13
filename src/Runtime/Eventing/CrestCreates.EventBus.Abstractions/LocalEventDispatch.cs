using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Shared typed dispatch core. Handlers are resolved from the provider of the current scope in
/// DI registration order; the first failure stops the sequence. The static base-class dispatch
/// difference of the legacy dispatcher is intentionally retired: dispatch is always keyed by
/// the exact runtime event type.
/// </summary>
public static class LocalEventDispatch
{
    public static async ValueTask InvokeHandlersAsync<TEvent>(
        IServiceProvider serviceProvider,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : ILocalEvent
        => await InvokeHandlersAsync(serviceProvider, @event, cancellationToken, expectedHandlerTypes: null)
            .ConfigureAwait(false);

    public static async ValueTask InvokeHandlersAsync<TEvent>(
        IServiceProvider serviceProvider,
        TEvent @event,
        CancellationToken cancellationToken,
        IReadOnlyList<Type>? expectedHandlerTypes)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(@event);

        var handlers = serviceProvider.GetServices<ILocalEventHandler<TEvent>>().ToList();

        ValidateRequiredHandlers(typeof(TEvent), expectedHandlerTypes, handlers);

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validates that every expected handler type is satisfied by a resolved instance. Called by the
    /// dispatcher with the binding's merged expected-handler list so that typed expected-handler
    /// diagnostics apply uniformly after contributor merge — independent of which contributor's
    /// invoker happened to be retained.
    /// </summary>
    public static void ValidateRequiredHandlers(
        Type eventType,
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<object> handlerInstances)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handlerInstances);

        if (expectedHandlerTypes is null)
        {
            return;
        }

        foreach (var expectedHandlerType in expectedHandlerTypes)
        {
            var satisfied = false;
            foreach (var handler in handlerInstances)
            {
                if (expectedHandlerType.IsInstanceOfType(handler))
                {
                    satisfied = true;
                    break;
                }
            }

            if (!satisfied)
            {
                throw new LocalEventRequiredHandlerMissingException(eventType, expectedHandlerType);
            }
        }
    }
}
