using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Compile-time closed preparation entry of a local event registration. Prepare resolves the actual
/// handler instances exactly once inside the caller's execution scope, validates the merged required
/// list and returns a single-use invocation that holds those instances and the payload; execution
/// never touches the service provider again.
/// </summary>
public abstract class LocalEventInvoker
{
    /// <summary>True for host-side <c>typeof</c> declarations without a typed dispatcher.</summary>
    public virtual bool IsInert => false;

    public abstract LocalEventPreparedInvocation Prepare(
        IServiceProvider serviceProvider,
        ILocalEvent payload,
        IReadOnlyList<Type> requiredHandlers);
}

/// <summary>
/// One prepared, single-use execution batch: the exact handler instances resolved during Prepare and
/// the typed payload. ExecuteAsync only iterates this array; it is not a cache or registry member.
/// </summary>
public abstract class LocalEventPreparedInvocation
{
    public abstract int HandlerCount { get; }

    /// <summary>True once the first handler invocation has been entered.</summary>
    public abstract bool AnyHandlerInvoked { get; }

    public abstract ValueTask ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class LocalEventInvoker<TEvent> : LocalEventInvoker
    where TEvent : ILocalEvent
{
    public override LocalEventPreparedInvocation Prepare(
        IServiceProvider serviceProvider,
        ILocalEvent payload,
        IReadOnlyList<Type> requiredHandlers)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(payload);

        if (payload is not TEvent typedEvent)
        {
            throw new InvalidOperationException(
                $"Local event invoker for '{typeof(TEvent).FullName}' received an instance of " +
                $"'{payload.GetType().FullName}'. The dispatch registry resolved the wrong binding.");
        }

        // The single resolution of this execution: the exact instances that will run.
        var handlers = serviceProvider.GetServices<ILocalEventHandler<TEvent>>().ToArray();
        LocalEventDispatch.ValidateRequiredHandlers(requiredHandlers, handlers);

        return new PreparedInvocation(handlers, typedEvent);
    }

    private sealed class PreparedInvocation : LocalEventPreparedInvocation
    {
        private readonly ILocalEventHandler<TEvent>[] _handlers;
        private readonly TEvent _payload;
        private int _executed;

        public PreparedInvocation(ILocalEventHandler<TEvent>[] handlers, TEvent payload)
        {
            _handlers = handlers;
            _payload = payload;
        }

        public override int HandlerCount => _handlers.Length;

        private bool _anyHandlerInvoked;

        public override bool AnyHandlerInvoked => _anyHandlerInvoked;

        public override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _executed, 1) == 1)
            {
                throw new InvalidOperationException(
                    "A prepared local event invocation is single-use; it cannot be executed again.");
            }

            foreach (var handler in _handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _anyHandlerInvoked = true;
                await handler.HandleAsync(_payload, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Placeholder invoker for host-side <c>typeof</c> declarations: such a declaration carries only
/// membership/codec capability. Handler fan-out comes from typing assemblies that contribute
/// their own typed registrations; with no such contribution the event is a declared zero-handler
/// notification (no-op).
/// </summary>
internal sealed class InertLocalEventInvoker : LocalEventInvoker
{
    public static readonly InertLocalEventInvoker Instance = new();

    private InertLocalEventInvoker()
    {
    }

    public override bool IsInert => true;

    public override LocalEventPreparedInvocation Prepare(
        IServiceProvider serviceProvider,
        ILocalEvent payload,
        IReadOnlyList<Type> requiredHandlers)
        => InertPreparedInvocation.Instance;

    private sealed class InertPreparedInvocation : LocalEventPreparedInvocation
    {
        public static readonly InertPreparedInvocation Instance = new();

        private InertPreparedInvocation()
        {
        }

        public override int HandlerCount => 0;

        public override bool AnyHandlerInvoked => false;

        public override ValueTask ExecuteAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
