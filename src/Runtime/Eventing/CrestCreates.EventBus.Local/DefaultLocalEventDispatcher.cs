using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local;

public class DefaultLocalEventDispatcher : ILocalEventDispatcher
{
    private static readonly MethodInfo DispatchTypedMethod =
        typeof(DefaultLocalEventDispatcher).GetMethod(nameof(DispatchTypedAsync), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Could not locate {nameof(DispatchTypedAsync)}.");

    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, Func<ILocalEvent, CancellationToken, Task>> _dispatchers = new();

    public DefaultLocalEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task DispatchAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var dispatcher = _dispatchers.GetOrAdd(@event.GetType(), CreateDispatcher);
        return dispatcher(@event, cancellationToken);
    }

    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        return DispatchTypedAsync(@event, cancellationToken);
    }

    private Func<ILocalEvent, CancellationToken, Task> CreateDispatcher(Type eventType)
    {
        var method = DispatchTypedMethod.MakeGenericMethod(eventType);

        return (localEvent, cancellationToken) =>
            (Task)method.Invoke(this, new object[] { localEvent, cancellationToken })!;
    }

    private async Task DispatchTypedAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : ILocalEvent
    {
        foreach (var handler in _serviceProvider.GetServices<ILocalEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}
