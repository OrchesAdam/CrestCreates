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

        if (expectedHandlerTypes is not null)
        {
            foreach (var expectedHandlerType in expectedHandlerTypes)
            {
                var satisfied = false;
                foreach (var handler in handlers)
                {
                    if (expectedHandlerType.IsInstanceOfType(handler))
                    {
                        satisfied = true;
                        break;
                    }
                }

                if (!satisfied)
                {
                    throw new LocalEventRequiredHandlerMissingException(typeof(TEvent), expectedHandlerType);
                }
            }
        }

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}
