using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

// ── Test events and handlers (top-level for the generators) ──

[LocalEvent(Replayable = true)]
internal sealed class HardeningRecorderEvent : DomainEvent
{
}

internal sealed class HardeningFailingHandler : ILocalEventHandler<HardeningRecorderEvent>
{
    public Task HandleAsync(HardeningRecorderEvent @event, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("hardening baseline failure");
}

[LocalEvent(Replayable = true)]
internal sealed class HardeningTenantProbeEvent : DomainEvent
{
}

internal sealed class HardeningTenantProbeHandler(ICurrentTenant currentTenant)
    : ILocalEventHandler<HardeningTenantProbeEvent>
{
    public static string? LastObservedTenantId;

    public Task HandleAsync(HardeningTenantProbeEvent @event, CancellationToken cancellationToken = default)
    {
        LastObservedTenantId = currentTenant.Id;
        return Task.CompletedTask;
    }
}

[LocalEvent(Replayable = true)]
internal sealed class HardeningDecodeProbeEvent : DomainEvent
{
}

internal sealed class HardeningDecodeProbeHandler : ILocalEventHandler<HardeningDecodeProbeEvent>
{
    public Task HandleAsync(HardeningDecodeProbeEvent @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ── Context fakes (factories for the #146 S3 restore protocol) ──

internal sealed class HardeningTenantInfo(string id) : ITenantInfo
{
    public string Id { get; } = id;

    public string Name => Id;

    public string? ConnectionString => null;
}

internal sealed class HardeningCurrentTenant : ICurrentTenant
{
    private readonly string? _defaultId;

    public HardeningCurrentTenant(string? defaultId = null)
    {
        _defaultId = defaultId;
        Id = defaultId;
    }

    public ITenantInfo? Tenant { get; private set; }

    public string? Id { get; private set; }

    public Task<IDisposable> ChangeAsync(string tenantId)
    {
        var previous = Snapshot();
        Id = tenantId;
        Tenant = new HardeningTenantInfo(tenantId);
        return Task.FromResult<IDisposable>(new Restore(this, previous));
    }

    public IDisposable Change(ITenantInfo tenant)
    {
        var previous = Snapshot();
        Id = tenant.Id;
        Tenant = tenant;
        return new Restore(this, previous);
    }

    public void SetTenantId(string tenantId)
    {
        Id = tenantId;
        Tenant = new HardeningTenantInfo(tenantId);
    }

    public void SetHost()
    {
        Id = _defaultId;
        Tenant = null;
    }

    private (string? Id, ITenantInfo? Tenant) Snapshot() => (Id, Tenant);

    private sealed class Restore(HardeningCurrentTenant owner, (string? Id, ITenantInfo? Tenant) previous) : IDisposable
    {
        public void Dispose()
        {
            owner.Id = previous.Id;
            owner.Tenant = previous.Tenant;
        }
    }
}

internal sealed class HardeningTenantProvider : ITenantProvider
{
    private readonly Dictionary<string, ITenantInfo> _tenants = new(StringComparer.Ordinal);

    public void Add(string id) => _tenants[id] = new HardeningTenantInfo(id);

    public Task<ITenantInfo?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.TryGetValue(tenantId, out var tenant) ? tenant : null);
}

internal sealed class HardeningAuditContextAccessor : IAuditOperationContextAccessor
{
    private readonly AsyncLocal<AuditOperationContext?> _current = new();

    public HardeningAuditContextAccessor(string? correlationId = null)
    {
        if (correlationId is not null)
        {
            _current.Value = new AuditOperationContext
            {
                CorrelationId = correlationId,
                OperationId = Guid.NewGuid().ToString("N"),
                Actor = new AuditActor { Kind = "system", Id = "baseline-test" },
                InvocationSource = "baseline-test",
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

    private sealed class Restore(HardeningAuditContextAccessor owner, AuditOperationContext? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}

// ── Descriptor provider for the governance-version baseline ──

internal sealed class HardeningV2DescriptorProvider : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() =>
    [
        new GeneratedEventDescriptor
        {
            Id = "evt_hardening_recorder_v2",
            Name = "hardening.recorder.v2",
            Version = 2,
            State = DescriptorState.Active,
            PayloadType = typeof(HardeningRecorderEvent),
            Scope = EventScope.Local,
        },
    ];
}

/// <summary>
/// #146 S1 failing-baseline tests (recorder/replay side). Each asserts the designed behavior from
/// the hardening spec and fails against the #126 implementation, registering the real behavioral
/// error produced by the current code.
/// </summary>
public class LocalEventHardeningBaselineIntegrationTests
{
    private static string ReplayKeyOf<TEvent>()
        => typeof(TEvent).Assembly.GetName().Name + ":" + typeof(TEvent).FullName;

    private static DeadLetterMessage CreatePendingRecord<TEvent>(
        byte[] payload,
        string? replayKey = null,
        string? tenantId = null,
        string? correlationId = null,
        string? descriptorId = null,
        string eventName = "hardening",
        int eventVersion = 1,
        int retryCount = 0)
    {
        var now = DateTime.UtcNow;
        return new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventName: eventName,
            EventVersion: eventVersion,
            EventDescriptorId: descriptorId,
            CorrelationId: correlationId,
            TenantId: tenantId,
            Scope: EventScope.Local,
            PayloadTypeFullName: typeof(TEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "baseline failure",
            ExceptionType: typeof(InvalidOperationException).FullName,
            OccurredAt: now,
            FailedAt: now,
            RetryCount: retryCount,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending,
            LocalReplayKey: replayKey ?? ReplayKeyOf<TEvent>());
    }

    // ── C01 baseline ──

    [Fact]
    public async Task Recorder_must_capture_the_ambient_tenant_and_correlation()
    {
        var tenant = new HardeningCurrentTenant("hardening-tenant-A");
        var accessor = new HardeningAuditContextAccessor("hardening-correlation-146");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningRecorderEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<ICurrentTenant>(tenant);
                services.AddSingleton<IAuditOperationContextAccessor>(accessor);
                services.AddScoped<ILocalEventHandler<HardeningRecorderEvent>, HardeningFailingHandler>();
            });

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new HardeningRecorderEvent()));
        }

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var pending = await store.GetPendingAsync(0, 10, CancellationToken.None);
        var message = Assert.Single(pending);

        // Designed behavior: the failure record carries the authoritative ambient identity captured
        // at the publish boundary — not null.
        Assert.Equal("hardening-tenant-A", message.TenantId);
        Assert.Equal("hardening-correlation-146", message.CorrelationId);
    }

    // ── C01 baseline (replay side) ──

    [Fact]
    public async Task Replay_must_execute_under_the_recorded_tenant_not_the_callers_tenant()
    {
        var tenant = new HardeningCurrentTenant("hardening-caller-tenant-B");
        var tenantProvider = new HardeningTenantProvider();
        tenantProvider.Add("hardening-tenant-A");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningTenantProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<ICurrentTenant>(tenant);
                services.AddSingleton<ITenantProvider>(tenantProvider);
                services.AddScoped<ILocalEventHandler<HardeningTenantProbeEvent>, HardeningTenantProbeHandler>();
            });

        HardeningTenantProbeHandler.LastObservedTenantId = null;
        var record = CreatePendingRecord<HardeningTenantProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new HardeningTenantProbeEvent()),
            tenantId: "hardening-tenant-A",
            correlationId: "hardening-correlation-146");

        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(record, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(record, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        // Designed behavior: the handler executes under the recorded tenant (A), not the caller's (B).
        Assert.Equal("hardening-tenant-A", HardeningTenantProbeHandler.LastObservedTenantId);
    }

    // ── Governance/recorder separation baseline ──

    [Fact]
    public async Task Recorder_must_keep_the_accepted_identity_when_the_registry_reports_another_version()
    {
        var registry = new EventRegistry(
            new RegistryValidationEngine<GeneratedEventDescriptor>(
                Array.Empty<IRegistryValidator<GeneratedEventDescriptor>>()));
        registry.Build([new HardeningV2DescriptorProvider()]);

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningRecorderEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<IEventRegistry>(registry);
                services.AddScoped<ILocalEventHandler<HardeningRecorderEvent>, HardeningFailingHandler>();
            });

        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new HardeningRecorderEvent()));
        }

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var pending = await store.GetPendingAsync(0, 10, CancellationToken.None);
        var message = Assert.Single(pending);

        // Designed behavior: the record uses the accepted local identity (v1) instead of being
        // silently discarded because the advisory registry lookup returned another version.
        Assert.Equal(1, message.EventVersion);
        Assert.Null(message.EventDescriptorId);
    }

    // ── R01 baseline ──

    [Fact]
    public async Task Decode_failure_must_not_mark_retrying_nor_consume_the_retry_budget()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningDecodeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<HardeningDecodeProbeEvent>, HardeningDecodeProbeHandler>();
            });

        var broken = CreatePendingRecord<HardeningDecodeProbeEvent>(
            Encoding.UTF8.GetBytes("this-is-not-json"));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(broken, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(broken, CancellationToken.None);

        Assert.False(result.Success);
        var fresh = await store.GetByIdAsync(broken.MessageId, CancellationToken.None);
        Assert.NotNull(fresh);
        // Designed behavior: decode/identity/prepare complete before MarkRetrying — a corrupt payload
        // leaves the original state and budget untouched.
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }
}
