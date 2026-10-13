using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Compile-time closed typed invocation entry of a local event registration. Implementations
/// dispatch to handlers through the non-generic overload with a typed cast only; no reflection
/// or dynamic member access is involved.
/// </summary>
public abstract class LocalEventInvoker
{
    /// <summary>True for host-side <c>typeof</c> declarations without a typed dispatcher.</summary>
    public virtual bool IsInert => false;

    public abstract ValueTask InvokeAsync(
        IServiceProvider serviceProvider,
        ILocalEvent @event,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the current handler instances of the scope without invoking them; used by the
    /// replay core to run missing-handler prechecks before any state transition.
    /// </summary>
    public abstract IReadOnlyList<object> ProbeHandlerInstances(IServiceProvider serviceProvider);
}

public sealed class LocalEventInvoker<TEvent> : LocalEventInvoker
    where TEvent : ILocalEvent
{
    private readonly Func<IServiceProvider, TEvent, CancellationToken, ValueTask> _dispatch;

    public LocalEventInvoker(Func<IServiceProvider, TEvent, CancellationToken, ValueTask> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
    }

    public override ValueTask InvokeAsync(
        IServiceProvider serviceProvider,
        ILocalEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(@event);

        if (@event is not TEvent typedEvent)
        {
            throw new InvalidOperationException(
                $"Local event invoker for '{typeof(TEvent).FullName}' received an instance of " +
                $"'{@event.GetType().FullName}'. The dispatch registry resolved the wrong binding.");
        }

        return _dispatch(serviceProvider, typedEvent, cancellationToken);
    }

    public override IReadOnlyList<object> ProbeHandlerInstances(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetServices<ILocalEventHandler<TEvent>>().Cast<object>().ToArray();
    }
}

/// <summary>
/// Placeholder invoker for host-side <c>typeof</c> declarations: such a declaration carries only
/// membership/codec capability. Handler fan-out comes from typing assemblies that contribute
/// their own typed bindings; with no such contribution the event is a declared zero-handler
/// notification (no-op).
/// </summary>
internal sealed class InertLocalEventInvoker : LocalEventInvoker
{
    public static readonly InertLocalEventInvoker Instance = new();

    private InertLocalEventInvoker()
    {
    }

    public override bool IsInert => true;

    public override ValueTask InvokeAsync(
        IServiceProvider serviceProvider,
        ILocalEvent @event,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public override IReadOnlyList<object> ProbeHandlerInstances(IServiceProvider serviceProvider)
        => Array.Empty<object>();
}
