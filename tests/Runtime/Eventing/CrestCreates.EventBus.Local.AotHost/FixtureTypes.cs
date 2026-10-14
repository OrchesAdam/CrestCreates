using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

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

    // ── S4 scenario state ──

    private int _transientConstructions;
    private int _migratingCalls;
    private int _releaseHandled;
    private int _releaseDisposes;
    private int _governedCalls;

    private int _isolatedCalls;

    public int IsolatedCalls => Volatile.Read(ref _isolatedCalls);

    public void RecordIsolated() => Interlocked.Increment(ref _isolatedCalls);

    public int TransientConstructions => Volatile.Read(ref _transientConstructions);

    public int MigratingCalls => Volatile.Read(ref _migratingCalls);

    public int ReleaseHandled => Volatile.Read(ref _releaseHandled);

    public int ReleaseDisposes => Volatile.Read(ref _releaseDisposes);

    public int GovernedCalls => Volatile.Read(ref _governedCalls);

    public bool FailReleaseDispose { get; set; }

    public string? LastMigratingMessage { get; private set; }

    public object? LastTransientExecutedInstance { get; private set; }

    public string? LastObservedChannelTenantId { get; private set; }

    public string? LastObservedChannelCorrelationId { get; private set; }

    public void RecordTransientConstruction(object instance) => Interlocked.Increment(ref _transientConstructions);

    public void RecordTransientExecution(object instance)
    {
        LastTransientExecutedInstance = instance;
    }

    public void RecordMigrating(string message)
    {
        LastMigratingMessage = message;
        Interlocked.Increment(ref _migratingCalls);
    }

    public void RecordReleaseHandled() => Interlocked.Increment(ref _releaseHandled);

    public void RecordReleaseDispose() => Interlocked.Increment(ref _releaseDisposes);

    public void RecordGoverned() => Interlocked.Increment(ref _governedCalls);

    public void RecordChannelObservation(string? tenantId, string? correlationId)
    {
        LastObservedChannelTenantId = tenantId;
        LastObservedChannelCorrelationId = correlationId;
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
    private readonly ICurrentTenant _currentTenant;
    private readonly IAuditOperationContextAccessor _auditAccessor;

    public AotChannelHandler(
        AotControl control,
        ICurrentTenant currentTenant,
        IAuditOperationContextAccessor auditAccessor)
    {
        _control = control;
        _currentTenant = currentTenant;
        _auditAccessor = auditAccessor;
    }

    public Task HandleAsync(AotChannelEvent @event, CancellationToken cancellationToken = default)
    {
        _control.RecordChannelObservation(_currentTenant.Id, _auditAccessor.Current?.CorrelationId);
        if (_control.FailChannelEvent)
        {
            throw new InvalidOperationException($"AotChannelEvent handler failure for '{@event.Message}'");
        }

        _control.RecordChannel(@event.Message);
        return Task.CompletedTask;
    }
}

// ── S4 scenario extensions ──

[LocalEvent(Replayable = true, ReplayKey = "aot.migrating")]
[LocalEventReplayAlias("CrestCreates.EventBus.Local.AotHost:CrestCreates.EventBus.Local.AotHost.AotMigratingEvent", 1)]
internal sealed class AotMigratingEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

[LocalEvent(Replayable = true)]
internal sealed class AotTransientEvent : DomainEvent
{
}

[LocalEvent(Replayable = true)]
internal sealed class AotReleaseEvent : DomainEvent
{
}

/// <summary>
/// Governance probe declared manually in the governed scenario (the generated contributor is not
/// activated there); the attribute only provides the replay codec contract.
/// </summary>
[LocalEvent(Replayable = true)]
internal sealed class AotGovernedEvent : DomainEvent
{
}

/// <summary>Second typed context used to contribute divergent codec metadata for the conflict probe.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(AotProbeEvent))]
internal sealed partial class AotConflictCodecContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

internal sealed class AotTransientHandler : ILocalEventHandler<AotTransientEvent>
{
    private readonly AotControl _control;

    public AotTransientHandler(AotControl control)
    {
        _control = control;
        _control.RecordTransientConstruction(this);
    }

    public Task HandleAsync(AotTransientEvent @event, CancellationToken cancellationToken = default)
    {
        _control.RecordTransientExecution(this);
        return Task.CompletedTask;
    }
}

internal sealed class AotMigratingHandler : ILocalEventHandler<AotMigratingEvent>
{
    private readonly AotControl _control;

    public AotMigratingHandler(AotControl control) => _control = control;

    public Task HandleAsync(AotMigratingEvent @event, CancellationToken cancellationToken = default)
    {
        _control.RecordMigrating(@event.Message);
        return Task.CompletedTask;
    }
}

internal sealed class AotReleaseHandler : ILocalEventHandler<AotReleaseEvent>
{
    private readonly AotControl _control;
    private readonly AotReleaseProbe _probe;

    public AotReleaseHandler(AotControl control, AotReleaseProbe probe)
    {
        _control = control;
        _probe = probe;
    }

    public Task HandleAsync(AotReleaseEvent @event, CancellationToken cancellationToken = default)
    {
        _ = _probe.GetHashCode();
        _control.RecordReleaseHandled();
        return Task.CompletedTask;
    }
}

internal sealed class AotReleaseProbe(AotControl control) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        control.RecordReleaseDispose();
        if (control.FailReleaseDispose)
        {
            throw new InvalidOperationException("aot probe release failure");
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class AotGovernedHandler : ILocalEventHandler<AotGovernedEvent>
{
    private readonly AotControl _control;

    public AotGovernedHandler(AotControl control) => _control = control;

    public Task HandleAsync(AotGovernedEvent @event, CancellationToken cancellationToken = default)
    {
        _control.RecordGoverned();
        return Task.CompletedTask;
    }
}

internal sealed class AotDescriptorProvider(params GeneratedEventDescriptor[] descriptors) : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => descriptors;
}

// ── Deterministic in-memory context facilities (Spec §12: framework protocol evidence only) ──

internal sealed class AotTenantInfo(string id) : ITenantInfo
{
    public string Id { get; } = id;

    public string Name => Id;

    public string? ConnectionString => null;
}

internal sealed class AotTenantProvider : ITenantProvider
{
    private readonly Dictionary<string, ITenantInfo> _tenants = new(StringComparer.Ordinal);

    public void Add(string id) => _tenants[id] = new AotTenantInfo(id);

    public Task<ITenantInfo?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.TryGetValue(tenantId, out var tenant) ? tenant : null);
}

/// <summary>
/// Frame-isolated in-memory current tenant (AsyncLocal-backed like the production implementation):
/// a background consumer task keeps the execution context it started with, so a missing channel
/// restore is observable as host context instead of leaking the caller frame.
/// </summary>
internal sealed class AotCurrentTenant : ICurrentTenant
{
    private readonly AsyncLocal<TenantHolder?> _current = new();

    public ITenantInfo? Tenant => _current.Value?.Tenant;

    public string? Id => Tenant?.Id;

    public Task<IDisposable> ChangeAsync(string tenantId)
    {
        var previous = _current.Value;
        _current.Value = new TenantHolder { Tenant = new AotTenantInfo(tenantId) };
        return Task.FromResult<IDisposable>(new Restore(this, previous));
    }

    public IDisposable Change(ITenantInfo? tenant)
    {
        var previous = _current.Value;
        _current.Value = new TenantHolder { Tenant = tenant };
        return new Restore(this, previous);
    }

    public void SetTenantId(string tenantId)
    {
        _current.Value = new TenantHolder { Tenant = new AotTenantInfo(tenantId) };
    }

    private sealed class TenantHolder
    {
        public ITenantInfo? Tenant { get; set; }
    }

    private sealed class Restore(AotCurrentTenant owner, TenantHolder? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}

internal sealed class AotAuditAccessor : IAuditOperationContextAccessor
{
    private readonly AsyncLocal<AuditOperationContext?> _current = new();

    public AotAuditAccessor(string? correlationId = null)
    {
        if (correlationId is not null)
        {
            _current.Value = new AuditOperationContext
            {
                CorrelationId = correlationId,
                OperationId = Guid.NewGuid().ToString("N"),
                Actor = new AuditActor { Kind = "system", Id = "aot-fixture" },
                InvocationSource = "aot-fixture",
            };
        }
    }

    public AuditOperationContext? Current => _current.Value;

    public IDisposable Push(AuditOperationContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new Restore(this, previous);
    }

    private sealed class Restore(AotAuditAccessor owner, AuditOperationContext? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}

internal sealed class AotIdentityGenerator : IAuditIdentityGenerator
{
    public string CreateOperationId() => Guid.NewGuid().ToString("N");

    public string CreateAuditId() => Guid.NewGuid().ToString("N");
}

[LocalEvent(Replayable = true)]
internal sealed class AotIsolatedEvent : DomainEvent
{
}

internal sealed class AotIsolatedHandler : ILocalEventHandler<AotIsolatedEvent>
{
    private readonly AotControl _control;

    public AotIsolatedHandler(AotControl control) => _control = control;

    public Task HandleAsync(AotIsolatedEvent @event, CancellationToken cancellationToken = default)
    {
        _control.RecordIsolated();
        return Task.CompletedTask;
    }
}
