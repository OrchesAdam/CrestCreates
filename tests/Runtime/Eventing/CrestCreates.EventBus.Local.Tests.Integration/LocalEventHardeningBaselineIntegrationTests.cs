using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
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

internal sealed class HardeningTenantProbeHandler(ICurrentTenant currentTenant, IAuditOperationContextAccessor auditContext)
    : ILocalEventHandler<HardeningTenantProbeEvent>
{
    public static string? LastObservedTenantId;
    public static string? LastObservedCorrelationId;

    public Task HandleAsync(HardeningTenantProbeEvent @event, CancellationToken cancellationToken = default)
    {
        LastObservedTenantId = currentTenant.Id;
        LastObservedCorrelationId = auditContext.Current?.CorrelationId;
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

// Dedicated parameterless handler for the missing-facility negative baselines: today's replay must
// be able to fully execute the business path (no dependency on the absent facilities) so the
// assertion targets the missing admission rejection, not a DI activation error.
[LocalEvent(Replayable = true)]
internal sealed class HardeningNoFacilityProbeEvent : DomainEvent
{
}

internal sealed class HardeningNoFacilityProbeHandler : ILocalEventHandler<HardeningNoFacilityProbeEvent>
{
    public static int Executions;

    public Task HandleAsync(HardeningNoFacilityProbeEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Executions);
        return Task.CompletedTask;
    }
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

    public IDisposable Change(ITenantInfo? tenant)
    {
        var previous = Snapshot();
        Id = tenant?.Id;
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

internal sealed class HardeningAuditIdentityGenerator : IAuditIdentityGenerator
{
    public string CreateOperationId() => Guid.NewGuid().ToString("N");

    public string CreateAuditId() => Guid.NewGuid().ToString("N");
}

// ── H03 factory-mutation probe ──

/// <summary>
/// Manual-declaration-only event: it intentionally carries no [LocalEvent] attribute so the
/// assembly's generated codec contribution does not compete with the mutable manual factory that
/// this probe owns (one payload type, one origin).
/// </summary>
internal sealed class HardeningFactoryProbeEvent : DomainEvent
{
}

[JsonSerializable(typeof(HardeningFactoryProbeEvent))]
internal partial class HardeningFactoryProbeJsonContext : JsonSerializerContext;

internal sealed class HardeningFactoryProbeHandler : ILocalEventHandler<HardeningFactoryProbeEvent>
{
    public static Func<Task> Behavior = static () => Task.CompletedTask;
    public static int Calls;

    public Task HandleAsync(HardeningFactoryProbeEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Behavior();
    }
}

internal sealed class HardeningManualContributor(string contributorId, Action<LocalEventDispatchRegistrationBuilder> register)
    : ILocalEventRegistrationContributor
{
    public string ContributorId => contributorId;

    public void Register(LocalEventDispatchRegistrationBuilder builder) => register(builder);
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
        int retryCount = 0,
        int? localContextVersion = null)
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
            LocalReplayKey: replayKey ?? ReplayKeyOf<TEvent>(),
            LocalContextVersion: localContextVersion);
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
                var tenantProvider = new HardeningTenantProvider();
                tenantProvider.Add("hardening-tenant-A");
                services.AddSingleton<ICurrentTenant>(tenant);
                services.AddSingleton<ITenantProvider>(tenantProvider);
                services.AddSingleton<IAuditOperationContextAccessor>(accessor);
                services.AddSingleton<IAuditIdentityGenerator, HardeningAuditIdentityGenerator>();
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

        // The full recovery facilities required by the target contract (§7.2): async tenant
        // resolution, audit context push and identity generation for the new attempt.
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningTenantProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<ICurrentTenant>(tenant);
                services.AddSingleton<ITenantProvider>(tenantProvider);
                services.AddSingleton<IAuditOperationContextAccessor>(
                    new HardeningAuditContextAccessor("hardening-caller-correlation"));
                services.AddSingleton<IAuditIdentityGenerator, HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<HardeningTenantProbeEvent>, HardeningTenantProbeHandler>();
            });

        HardeningTenantProbeHandler.LastObservedTenantId = null;
        HardeningTenantProbeHandler.LastObservedCorrelationId = null;
        var record = CreatePendingRecord<HardeningTenantProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new HardeningTenantProbeEvent()),
            tenantId: "hardening-tenant-A",
            correlationId: "hardening-correlation-146");

        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(record, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(record, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        // Designed behavior: the handler executes under the recorded tenant (A) — not the caller's (B) —
        // and observes the restored correlation of the record.
        Assert.Equal("hardening-tenant-A", HardeningTenantProbeHandler.LastObservedTenantId);
        Assert.Equal("hardening-correlation-146", HardeningTenantProbeHandler.LastObservedCorrelationId);
        // The caller frame is restored after the attempt.
        Assert.Equal("hardening-caller-tenant-B", tenant.Id);
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

    // ── C05 baselines: missing recovery facilities must reject before handlers ──

    [Fact]
    public async Task Replay_with_nonnull_correlation_but_no_audit_facilities_must_reject_before_handlers()
    {
        // The recorded tenant must be resolvable so that the ONLY missing condition stays the audit
        // accessor/identity capability (R2-LE146-01): an unknown-tenant rejection must not be able to
        // make this test pass.
        var tenantProvider = new HardeningTenantProvider();
        tenantProvider.Add("hardening-tenant-A");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningNoFacilityProbeEvent>(replayable: true),
            configure: services =>
            {
                // Tenant facilities exist; audit accessor/identity are deliberately absent.
                services.AddSingleton<ICurrentTenant>(new HardeningCurrentTenant("hardening-tenant-A"));
                services.AddSingleton<ITenantProvider>(tenantProvider);
                services.AddScoped<ILocalEventHandler<HardeningNoFacilityProbeEvent>, HardeningNoFacilityProbeHandler>();
            });

        Interlocked.Exchange(ref HardeningNoFacilityProbeHandler.Executions, 0);
        var record = CreatePendingRecord<HardeningNoFacilityProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new HardeningNoFacilityProbeEvent()),
            tenantId: "hardening-tenant-A",
            correlationId: "hardening-correlation-146");

        // Fixture guard: the recorded tenant resolves and matches before execution, so a later
        // fixture drift cannot silently reintroduce an unknown-tenant rejection path.
        var resolvedTenant = await tenantProvider.GetTenantAsync(record.TenantId!, CancellationToken.None);
        Assert.NotNull(resolvedTenant);
        Assert.Equal(record.TenantId, resolvedTenant!.Id);

        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(record, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(record, CancellationToken.None);

        // Designed behavior: a non-null persisted correlation must be restorable; with no audit
        // accessor/identity capability the attempt is rejected before any handler runs and neither
        // state nor budget change (no silent downgrade to an unassociated execution).
        Assert.False(result.Success);
        Assert.Equal(0, HardeningNoFacilityProbeHandler.Executions);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Replay_with_nonnull_tenant_but_no_tenant_provider_must_reject_before_handlers()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<HardeningNoFacilityProbeEvent>(replayable: true),
            configure: services =>
            {
                // No ICurrentTenant/ITenantProvider: the recorded tenant cannot be validated/restored.
                services.AddSingleton<IAuditOperationContextAccessor>(
                    new HardeningAuditContextAccessor("hardening-caller-correlation"));
                services.AddSingleton<IAuditIdentityGenerator, HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<HardeningNoFacilityProbeEvent>, HardeningNoFacilityProbeHandler>();
            });

        Interlocked.Exchange(ref HardeningNoFacilityProbeHandler.Executions, 0);
        var record = CreatePendingRecord<HardeningNoFacilityProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new HardeningNoFacilityProbeEvent()),
            tenantId: "hardening-tenant-A",
            correlationId: "hardening-correlation-146");

        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(record, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(record, CancellationToken.None);

        // Designed behavior: a non-empty persisted TenantId without resolution capability is refused
        // before any handler runs — it must not fall back to host execution.
        Assert.False(result.Success);
        Assert.Equal(0, HardeningNoFacilityProbeHandler.Executions);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    // ── H03 baselines: factory mutation after freeze (independent of R01/G01) ──

    private static Func<System.Text.Json.Serialization.Metadata.JsonTypeInfo?> BuildMutableFactory()
    {
        var holder = new System.Text.Json.Serialization.Metadata.JsonTypeInfo?[]
        {
            HardeningFactoryProbeJsonContext.Default.HardeningFactoryProbeEvent,
        };
        MutableCodecHolder.Holder = holder;
        return () => Volatile.Read(ref MutableCodecHolder.Holder)[0];
    }

    private static ServiceProvider BuildFactoryMutationProvider()
    {
        return LocalEventTestComposition.Build(
            declare: _ => { },
            configure: services =>
            {
                var registration = LocalEventRegistration.Create<HardeningFactoryProbeEvent>(
                    new LocalEventInvoker<HardeningFactoryProbeEvent>(),
                    replayVersion: 1,
                    replayable: true,
                    codecFactory: BuildMutableFactory(),
                    expectedHandlerTypes: null,
                    legacyIdentities: null,
                    sourceAssembly: "Hardening.Baseline");
                services.AddSingleton<ILocalEventRegistrationContributor>(
                    new HardeningManualContributor("factory-mutation", builder => builder.Add(registration)));
                services.AddScoped<ILocalEventHandler<HardeningFactoryProbeEvent>, HardeningFactoryProbeHandler>();
            });
    }

    [Fact]
    public async Task Factory_mutation_after_freeze_must_not_break_failure_recording()
    {
        await using var provider = BuildFactoryMutationProvider();
        // Freeze first with the valid codec, then mutate the factory's product — the frozen
        // execution contract must survive (no runtime re-evaluation).
        provider.GetRequiredService<ILocalEventDispatchRegistry>();
        Volatile.Write(ref MutableCodecHolder.Holder, [null]);

        HardeningFactoryProbeHandler.Behavior = static () => throw new InvalidOperationException("hardening factory failure");
        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new HardeningFactoryProbeEvent()));
        }

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var pending = await store.GetPendingAsync(0, 10, CancellationToken.None);
        // Designed behavior: failure recording uses the frozen codec; a mutated factory must not make
        // the record disappear.
        Assert.Single(pending);
    }

    [Fact]
    public async Task Factory_mutation_after_freeze_must_not_break_replay()
    {
        await using var provider = BuildFactoryMutationProvider();
        provider.GetRequiredService<ILocalEventDispatchRegistry>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new HardeningFactoryProbeEvent(),
            HardeningFactoryProbeJsonContext.Default.HardeningFactoryProbeEvent);
        Volatile.Write(ref MutableCodecHolder.Holder, [null]);

        HardeningFactoryProbeHandler.Behavior = static () => Task.CompletedTask;
        Interlocked.Exchange(ref HardeningFactoryProbeHandler.Calls, 0);
        var record = CreatePendingRecord<HardeningFactoryProbeEvent>(
            payload, localContextVersion: LocalEventExecutionContextSnapshot.CurrentContextVersion);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        await store.EnqueueAsync(record, CancellationToken.None);

        var executor = provider.GetRequiredService<ILocalDeadLetterReplayExecutor>();
        var result = await executor.ExecuteAsync(record, CancellationToken.None);

        // Designed behavior: replay decodes with the frozen codec and executes; a mutated factory must
        // not turn the attempt into a rejection.
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, HardeningFactoryProbeHandler.Calls);
    }
}

internal static class MutableCodecHolder
{
    public static System.Text.Json.Serialization.Metadata.JsonTypeInfo?[] Holder = [];
}
