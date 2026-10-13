using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.AotHost;

[LocalEvent(Replayable = true)]
internal sealed class AotProbeEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

[LocalEvent(Replayable = true)]
internal sealed class AotFailEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>Declared replayable with no handler: replay must be refused, never marked successful.</summary>
[LocalEvent(Replayable = true)]
internal sealed class AotOrphanEvent : DomainEvent
{
}

[LocalEvent(Replayable = true)]
internal sealed class AotChannelEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>No declaration and no handler: publishing must fail deterministically.</summary>
internal sealed class AotUndeclaredEvent : DomainEvent
{
}

/// <summary>Used only for the replayable-without-codec freeze refusal; never declared or handled.</summary>
internal sealed class AotNoCodecEvent : DomainEvent
{
}

internal sealed class AotControl
{
    private int _failFailEvent = 1;
    private int _failChannelEvent = 1;
    private int _probeCalls;
    private int _failEventCalls;
    private int _channelCalls;

    public bool FailFailEvent
    {
        get => Volatile.Read(ref _failFailEvent) == 1;
        set => Volatile.Write(ref _failFailEvent, value ? 1 : 0);
    }

    public bool FailChannelEvent
    {
        get => Volatile.Read(ref _failChannelEvent) == 1;
        set => Volatile.Write(ref _failChannelEvent, value ? 1 : 0);
    }

    public int ProbeCalls => Volatile.Read(ref _probeCalls);

    public int FailEventCalls => Volatile.Read(ref _failEventCalls);

    public int ChannelCalls => Volatile.Read(ref _channelCalls);

    public string? LastFailEventMessage { get; private set; }

    public string? LastChannelMessage { get; private set; }

    public void RecordProbe() => Interlocked.Increment(ref _probeCalls);

    public void RecordFailEvent(string message)
    {
        LastFailEventMessage = message;
        Interlocked.Increment(ref _failEventCalls);
    }

    public void RecordChannel(string message)
    {
        LastChannelMessage = message;
        Interlocked.Increment(ref _channelCalls);
    }
}

internal sealed class AotScopedProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed class AotProbeHandler : ILocalEventHandler<AotProbeEvent>
{
    private readonly AotControl _control;
    private readonly AotScopedProbe _probe;

    public AotProbeHandler(AotControl control, AotScopedProbe probe)
    {
        _control = control;
        _probe = probe;
    }

    public Guid ObservedProbeId { get; private set; }

    public string? LastMessage { get; private set; }

    public Task HandleAsync(AotProbeEvent @event, CancellationToken cancellationToken = default)
    {
        ObservedProbeId = _probe.Id;
        LastMessage = @event.Message;
        _control.RecordProbe();
        return Task.CompletedTask;
    }
}

internal sealed class AotFailHandler : ILocalEventHandler<AotFailEvent>
{
    private readonly AotControl _control;

    public AotFailHandler(AotControl control) => _control = control;

    public Task HandleAsync(AotFailEvent @event, CancellationToken cancellationToken = default)
    {
        if (_control.FailFailEvent)
        {
            throw new InvalidOperationException($"AotFailEvent handler failure for '{@event.Message}'");
        }

        _control.RecordFailEvent(@event.Message);
        return Task.CompletedTask;
    }
}

internal sealed class AotChannelHandler : ILocalEventHandler<AotChannelEvent>
{
    private readonly AotControl _control;

    public AotChannelHandler(AotControl control) => _control = control;

    public Task HandleAsync(AotChannelEvent @event, CancellationToken cancellationToken = default)
    {
        if (_control.FailChannelEvent)
        {
            throw new InvalidOperationException($"AotChannelEvent handler failure for '{@event.Message}'");
        }

        _control.RecordChannel(@event.Message);
        return Task.CompletedTask;
    }
}
