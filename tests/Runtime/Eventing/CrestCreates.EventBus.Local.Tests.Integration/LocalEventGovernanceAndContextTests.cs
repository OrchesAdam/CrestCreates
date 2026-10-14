using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using CrestCreates.EventBus.RabbitMQ.Extensions;
using CrestCreates.Generated;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

// ── S3 probe events and handlers (top-level for the generator) ──

internal sealed class GovernedProbeEvent : DomainEvent
{
}

internal sealed class GovernedProbeHandler : ILocalEventHandler<GovernedProbeEvent>
{
    public static int Calls;

    public Task HandleAsync(GovernedProbeEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

[LocalEvent(Replayable = true)]
internal sealed class GovernedHistoryProbeEvent : DomainEvent
{
}

[LocalEvent(Replayable = true)]
internal sealed class GovernedHistoryAdoptingEvent : DomainEvent
{
}

internal sealed class GovernedHistoryHandler : ILocalEventHandler<GovernedHistoryProbeEvent>
{
    public static int Calls;

    public Task HandleAsync(GovernedHistoryProbeEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class GovernedHistoryAdoptingHandler : ILocalEventHandler<GovernedHistoryAdoptingEvent>
{
    public static int Calls;

    public Task HandleAsync(GovernedHistoryAdoptingEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class AliasSourceEvent : DomainEvent
{
}

internal sealed class AliasTargetEvent : DomainEvent
{
}

internal sealed class AliasSourceHandler : ILocalEventHandler<AliasSourceEvent>
{
    public static int Calls;

    public Task HandleAsync(AliasSourceEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class AliasTargetHandler : ILocalEventHandler<AliasTargetEvent>
{
    public static int Calls;

    public Task HandleAsync(AliasTargetEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

[LocalEvent(Replayable = true)]
internal sealed class MigratingKeyEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

internal sealed class MigratingKeyHandler : ILocalEventHandler<MigratingKeyEvent>
{
    public static int Calls;
    public static string? LastMessage;

    public Task HandleAsync(MigratingKeyEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        LastMessage = @event.Message;
        return Task.CompletedTask;
    }
}

[LocalEvent(Replayable = true)]
internal sealed class RejectingCheckEvent : DomainEvent
{
}

internal sealed class RejectingCheckHandler : ILocalEventHandler<RejectingCheckEvent>
{
    public static int Calls;

    public Task HandleAsync(RejectingCheckEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class RejectingExecutionCheck : ILocalEventExecutionCheck
{
    public Task<LocalEventExecutionCheckResult> CheckAsync(
        LocalEventExecutionCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(LocalEventExecutionCheckResult.Reject("ProbeCheckRejected", "probe check refuses this event"));
}

internal sealed class NeverRegisteredExecutionCheck : ILocalEventExecutionCheck
{
    public Task<LocalEventExecutionCheckResult> CheckAsync(
        LocalEventExecutionCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(LocalEventExecutionCheckResult.Pass);
}

internal sealed class ThrowingEventValidator : IEventValidator
{
    public void ValidateOrThrow(string eventName, object? payload)
        => throw new InvalidOperationException($"probe validator refuses '{eventName}'");

    public ValidationResult Validate(string eventName, object? payload)
        => new(false, default, null);
}

[LocalEvent(Replayable = true)]
internal sealed class ModeProbeEvent : DomainEvent
{
}

internal sealed class ModeProbeHandler(CurrentTenantProbeState state) : ILocalEventHandler<ModeProbeEvent>
{
    public Task HandleAsync(ModeProbeEvent @event, CancellationToken cancellationToken = default)
    {
        state.ObservedTenantId = state.CurrentTenant?.Id;
        state.Calls++;
        return Task.CompletedTask;
    }
}

internal sealed class CurrentTenantProbeState
{
    public IServiceProvider? Provider { get; set; }

    public CrestCreates.MultiTenancy.Abstract.ICurrentTenant? CurrentTenant
        => Provider?.GetService(typeof(CrestCreates.MultiTenancy.Abstract.ICurrentTenant))
            as CrestCreates.MultiTenancy.Abstract.ICurrentTenant;

    public string? ObservedTenantId { get; set; }

    public int Calls { get; set; }
}

internal sealed class FlakyTenantProvider : CrestCreates.MultiTenancy.Abstract.ITenantProvider
{
    private readonly Dictionary<string, CrestCreates.MultiTenancy.Abstract.ITenantInfo> _tenants = new(StringComparer.Ordinal);

    public void Add(string id)
        => _tenants[id] = new HardeningTenantInfo(id);

    public void AddWithDifferentId(string requestedId, string resolvedId)
        => _tenants[requestedId] = new HardeningTenantInfo(resolvedId);

    public Task<CrestCreates.MultiTenancy.Abstract.ITenantInfo?> GetTenantAsync(
        string tenantId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tenants.TryGetValue(tenantId, out var tenant) ? tenant : null);
}

internal sealed class ProbeDescriptorProvider(params GeneratedEventDescriptor[] descriptors) : IEventDescriptorProvider
{
    public IReadOnlyList<GeneratedEventDescriptor> GetDescriptors() => descriptors;
}

/// <summary>
/// #146 S3: governance admission, stable keys/aliases, bootstrap readiness, tenant-mode matrix and
/// context capture/restore behavior across publish, channel and replay entries.
/// </summary>
public class LocalEventGovernanceAndContextTests
{
    private static LocalEventPolicyOptions Governed(bool adoptGovernedHistory = false, int version = 1)
        => new()
        {
            Governance = LocalEventGovernance.Required,
            AdoptGovernedHistory = adoptGovernedHistory,
        };

    private static GeneratedEventDescriptor Descriptor(
        Type payloadType, int version, DescriptorState state, string name = "governance.probe", string? id = null)
        => new()
        {
            Id = id ?? $"evt_{name.Replace('.', '_')}_v{version}",
            Name = name,
            Version = version,
            State = state,
            PayloadType = payloadType,
            Scope = EventScope.Local,
        };

    private static EventRegistry BuildRegistry(params IEventDescriptorProvider[] providers)
    {
        var registry = new EventRegistry(
            new RegistryValidationEngine<GeneratedEventDescriptor>(
                Array.Empty<IRegistryValidator<GeneratedEventDescriptor>>()));
        registry.Build(providers);
        return registry;
    }

    // ── I01/I02/I03: stable keys and replay aliases ──

    [Fact]
    public void Stable_key_freezes_verbatim_while_unset_keys_keep_the_deterministic_default()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder =>
            {
                builder.Declare<ModeProbeEvent>(
                    policy: new LocalEventPolicyOptions { ReplayKey = "orders.mode-probe" });
                builder.Declare<GovernedProbeEvent>();
            });

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        Assert.True(registry.TryGet(typeof(ModeProbeEvent), out var customized));
        Assert.Equal("orders.mode-probe", customized.LocalReplayKey);
        Assert.True(registry.TryGet(typeof(GovernedProbeEvent), out var defaulted));
        Assert.Equal(LocalEventIdentity.BuildLocalReplayKey(typeof(GovernedProbeEvent)), defaulted.LocalReplayKey);
    }

    [Fact]
    public async Task Default_key_records_migrate_to_a_stable_key_via_the_explicit_replay_alias()
    {
        var defaultKey = LocalEventIdentity.BuildLocalReplayKey(typeof(MigratingKeyEvent));
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<MigratingKeyEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions
                {
                    ReplayKey = "orders.migrating-key",
                    ReplayAliases = new[] { new LocalEventReplayAlias(defaultKey, 1) },
                }),
            configure: services =>
            {
                services.AddSingleton<MigratingKeyHandler>();
                services.AddSingleton<ILocalEventHandler<MigratingKeyEvent>>(
                    sp => sp.GetRequiredService<MigratingKeyHandler>());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<MigratingKeyEvent>(
            LocalEventTestComposition.SerializeWithCodec(new MigratingKeyEvent { Message = "old-row" }),
            defaultKey);
        await store.EnqueueAsync(record, CancellationToken.None);
        Interlocked.Exchange(ref MigratingKeyHandler.Calls, 0);
        MigratingKeyHandler.LastMessage = null;

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, MigratingKeyHandler.Calls);
        Assert.Equal("old-row", MigratingKeyHandler.LastMessage);
        // The original row keeps its historical key; only future records use the stable key.
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(defaultKey, fresh!.LocalReplayKey);
    }

    [Fact]
    public async Task Unknown_nonempty_replay_key_does_not_fall_back_to_payload_identity()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(new HandlerControl());
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent()),
            replayKey: "Bogus.Assembly:Bogus.Event");
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UnknownIdentity", result.ProblemCode);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public void Alias_version_must_equal_the_target_replay_version()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<AliasSourceEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions
                {
                    ReplayAliases = new[] { new LocalEventReplayAlias("legacy:alias", 2) },
                }));

        var failure = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        Assert.Contains(failure.Violations, violation => violation.Contains("must use the declaration's replay version"));
    }

    [Fact]
    public void Replay_alias_colliding_with_another_events_current_key_fails_at_freeze()
    {
        var currentKey = LocalEventIdentity.BuildLocalReplayKey(typeof(AliasSourceEvent));
        using var provider = LocalEventTestComposition.Build(
            declare: builder =>
            {
                builder.Declare<AliasSourceEvent>();
                builder.Declare<AliasTargetEvent>(
                    policy: new LocalEventPolicyOptions
                    {
                        ReplayAliases = new[] { new LocalEventReplayAlias(currentKey, 1) },
                    });
            });

        var failure = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        Assert.Contains(failure.Violations, violation => violation.Contains("collides with the current identity"));
    }

    // ── G02/G06: governance readiness, admission and bootstrap ──

    private static ServiceProvider BuildGovernanceProvider(
        Action<LocalEventDispatchRegistrationBuilder> declare,
        Action<IServiceCollection>? configure = null,
        bool hosted = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch(declare);
        configure?.Invoke(services);
        if (hosted)
        {
            services.AddCrestLocalEventGovernance();
        }
        else
        {
            services.AddEventKernel();
        }

        LocalEventJsonCodecContributions.AddCodecs(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Governed_events_reject_before_handlers_without_a_built_registry_and_recover_after_bootstrap()
    {
        await using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedProbeEvent>(policy: Governed()),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<GovernedProbeEvent>, GovernedProbeHandler>();
            });

        Interlocked.Exchange(ref GovernedProbeHandler.Calls, 0);
        await using (var scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => bus.PublishAsync(new GovernedProbeEvent()));
            Assert.Equal("GovernanceRegistryNotReady", failure.ProblemCode);
        }

        Assert.Equal(0, GovernedProbeHandler.Calls);

        // "Equivalent initialization": the caller drives the existing registry bootstrap; the
        // not-ready state was not cached, so the retry passes.
        provider.GetRequiredService<EventRegistry>().Build(new IEventDescriptorProvider[]
        {
            new ProbeDescriptorProvider(Descriptor(typeof(GovernedProbeEvent), 1, DescriptorState.Active, "governance.ready")),
        });

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new GovernedProbeEvent());
        }

        Assert.Equal(1, GovernedProbeHandler.Calls);
    }

    [Fact]
    public async Task Governed_replay_requires_the_built_registry_before_mark_retrying()
    {
        await using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true, policy: Governed()),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, CountingDispatchHandler>();
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);
        Interlocked.Exchange(ref CountingDispatchHandler.Calls, 0);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("GovernanceRegistryNotReady", result.ProblemCode);
        Assert.Equal(0, CountingDispatchHandler.Calls);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Governed_event_with_inactive_exact_version_is_refused_even_with_another_active_version()
    {
        var registry = BuildRegistry(new ProbeDescriptorProvider(
            Descriptor(typeof(GovernedProbeEvent), 1, DescriptorState.Deprecated, "governance.probe"),
            Descriptor(typeof(GovernedProbeEvent), 2, DescriptorState.Active, "governance.probe")));

        using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedProbeEvent>(policy: Governed()),
            configure: services =>
            {
                services.AddSingleton(registry);
                services.AddScoped<ILocalEventHandler<GovernedProbeEvent>, GovernedProbeHandler>();
            });

        Interlocked.Exchange(ref GovernedProbeHandler.Calls, 0);
        using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new GovernedProbeEvent()));
        Assert.Equal("GovernedDescriptorInactive", failure.ProblemCode);
        Assert.Equal(0, GovernedProbeHandler.Calls);
    }

    [Fact]
    public async Task Governed_replay_with_mismatched_persisted_descriptor_id_is_refused()
    {
        var descriptor = Descriptor(typeof(ReplayableDispatchEvent), 1, DescriptorState.Active, "governance.mismatch", "evt_expected");
        var registry = BuildRegistry(new ProbeDescriptorProvider(descriptor));

        await using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true, policy: Governed()),
            configure: services =>
            {
                services.AddSingleton(registry);
                services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, CountingDispatchHandler>();
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        record = record with { EventDescriptorId = "evt_other" };
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("GovernedDescriptorMismatch", result.ProblemCode);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
    }

    [Fact]
    public async Task Governed_history_requires_explicit_adoption_and_then_executes()
    {
        var registry = BuildRegistry(new ProbeDescriptorProvider(
            Descriptor(typeof(GovernedHistoryProbeEvent), 1, DescriptorState.Active, "governance.history"),
            Descriptor(typeof(GovernedHistoryAdoptingEvent), 1, DescriptorState.Active, "governance.adopting")));

        // Refused: a governed target with a null-descriptor historical row and no adoption mapping.
        await using (var strictProvider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedHistoryProbeEvent>(replayable: true, policy: Governed()),
            configure: services =>
            {
                services.AddSingleton(registry);
                services.AddScoped<ILocalEventHandler<GovernedHistoryProbeEvent>, GovernedHistoryHandler>();
            }))
        {
            var store = strictProvider.GetRequiredService<IDeadLetterStore>();
            var record = LocalEventTestComposition.CreateModernMessage<GovernedHistoryProbeEvent>(
                LocalEventTestComposition.SerializeWithCodec(new GovernedHistoryProbeEvent()),
                LocalEventIdentity.BuildLocalReplayKey(typeof(GovernedHistoryProbeEvent)));
            await store.EnqueueAsync(record, CancellationToken.None);
            Interlocked.Exchange(ref GovernedHistoryHandler.Calls, 0);

            var result = await strictProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(record, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("GovernedHistoryAdoptionMissing", result.ProblemCode);
            Assert.Equal(0, GovernedHistoryHandler.Calls);
        }

        // Adopted: the explicit per-event mapping binds the current same-version descriptor.
        await using (var adoptingProvider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedHistoryAdoptingEvent>(
                replayable: true, policy: Governed(adoptGovernedHistory: true)),
            configure: services =>
            {
                services.AddSingleton(registry);
                services.AddScoped<ILocalEventHandler<GovernedHistoryAdoptingEvent>, GovernedHistoryAdoptingHandler>();
            }))
        {
            var store = adoptingProvider.GetRequiredService<IDeadLetterStore>();
            var record = LocalEventTestComposition.CreateModernMessage<GovernedHistoryAdoptingEvent>(
                LocalEventTestComposition.SerializeWithCodec(new GovernedHistoryAdoptingEvent()),
                LocalEventIdentity.BuildLocalReplayKey(typeof(GovernedHistoryAdoptingEvent)));
            await store.EnqueueAsync(record, CancellationToken.None);
            Interlocked.Exchange(ref GovernedHistoryAdoptingHandler.Calls, 0);

            var result = await adoptingProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(record, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, GovernedHistoryAdoptingHandler.Calls);
        }
    }

    [Fact]
    public void Declared_execution_check_without_di_registration_fails_freeze()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<RejectingCheckEvent>(
                policy: new LocalEventPolicyOptions
                {
                    ExecutionCheckTypes = new[] { typeof(NeverRegisteredExecutionCheck) },
                }));

        var failure = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        Assert.Contains(failure.Violations, violation => violation.Contains("has no DI registration"));
    }

    [Fact]
    public async Task Execution_check_rejection_blocks_publish_dispatch_and_replay_uniformly()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<RejectingCheckEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions
                {
                    ExecutionCheckTypes = new[] { typeof(RejectingExecutionCheck) },
                }),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<RejectingCheckEvent>, RejectingCheckHandler>();
                services.AddScoped<RejectingExecutionCheck>();
            });

        Interlocked.Exchange(ref RejectingCheckHandler.Calls, 0);

        await using (var scope = provider.CreateAsyncScope())
        {
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new RejectingCheckEvent()));
            Assert.Equal("ProbeCheckRejected", failure.ProblemCode);

            var dispatchFailure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>().DispatchAsync(new RejectingCheckEvent()));
            Assert.Equal("ProbeCheckRejected", dispatchFailure.ProblemCode);
        }

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<RejectingCheckEvent>(
            LocalEventTestComposition.SerializeWithCodec(new RejectingCheckEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(RejectingCheckEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ProbeCheckRejected", result.ProblemCode);
        Assert.Equal(0, RejectingCheckHandler.Calls);
    }

    [Fact]
    public async Task Activated_validator_runs_on_every_entry_and_missing_registration_fails_freeze()
    {
        using (var freezeProvider = LocalEventTestComposition.Build(
                   declare: builder => builder.Declare<RejectingCheckEvent>(
                       policy: new LocalEventPolicyOptions { UseEventValidator = true }),
                   configure: services =>
                   {
                       services.RemoveAll<IEventValidator>();
                   }))
        {
            var freezeFailure = Assert.Throws<LocalEventDispatchConfigurationException>(
                () => freezeProvider.GetRequiredService<ILocalEventDispatchRegistry>());
            Assert.Contains(freezeFailure.Violations, violation => violation.Contains("no IEventValidator registration exists"));
        }

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<RejectingCheckEvent>(
                policy: new LocalEventPolicyOptions { UseEventValidator = true }),
            configure: services =>
            {
                services.RemoveAll<IEventValidator>();
                services.AddSingleton<IEventValidator, ThrowingEventValidator>();
                services.AddScoped<ILocalEventHandler<RejectingCheckEvent>, RejectingCheckHandler>();
            });

        Interlocked.Exchange(ref RejectingCheckHandler.Calls, 0);
        using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new RejectingCheckEvent()));
        Assert.Equal("EventValidatorRejected", failure.ProblemCode);
        Assert.Equal(0, RejectingCheckHandler.Calls);
    }

    [Fact]
    public async Task Hosted_governance_composition_builds_the_registry_and_admits_governed_events()
    {
        await using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedProbeEvent>(policy: Governed()),
            configure: services =>
            {
                services.AddSingleton<IEventDescriptorProvider>(new ProbeDescriptorProvider(
                    Descriptor(typeof(GovernedProbeEvent), 1, DescriptorState.Active, "governance.hosted")));
                services.AddScoped<ILocalEventHandler<GovernedProbeEvent>, GovernedProbeHandler>();
            },
            hosted: true);

        var coordinator = provider.GetServices<IHostedService>().OfType<BootstrapCoordinator>().Single();
        await coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(RegistryState.Built, provider.GetRequiredService<EventRegistry>().State);

        Interlocked.Exchange(ref GovernedProbeHandler.Calls, 0);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new GovernedProbeEvent());
        Assert.Equal(1, GovernedProbeHandler.Calls);
    }

    [Fact]
    public async Task Hosted_governance_with_zero_descriptor_providers_fails_startup_with_an_actionable_error()
    {
        await using var provider = BuildGovernanceProvider(
            declare: builder => builder.Declare<GovernedProbeEvent>(policy: Governed()),
            hosted: true);

        var coordinator = provider.GetServices<IHostedService>().OfType<BootstrapCoordinator>().Single();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(CancellationToken.None));
        Assert.Contains("AddEventDescriptors", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_governance_coexists_with_rabbitmq_composition_in_both_registration_orders()
    {
        static (ServiceProvider Provider, IServiceCollection Services) Build(bool localFirst)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            void Local()
            {
                services.AddCrestLocalEventDispatch(builder => builder.Declare<GovernedProbeEvent>(policy: Governed()));
                services.AddCrestLocalEventGovernance();
                services.AddSingleton<IEventDescriptorProvider>(new ProbeDescriptorProvider(
                    Descriptor(typeof(GovernedProbeEvent), 1, DescriptorState.Active, "governance.coexist")));
                services.AddScoped<ILocalEventHandler<GovernedProbeEvent>, GovernedProbeHandler>();
            }

            if (localFirst)
            {
                Local();
                services.AddRabbitMqEventBus();
            }
            else
            {
                services.AddRabbitMqEventBus();
                Local();
            }

            return (services.BuildServiceProvider(), services);
        }

        foreach (var localFirst in new[] { true, false })
        {
            var (provider, _) = Build(localFirst);
            await using (provider)
            {
                var coordinators = provider.GetServices<IHostedService>().OfType<BootstrapCoordinator>().ToList();
                Assert.Single(coordinators);

                await coordinators[0].StartAsync(CancellationToken.None);
                Assert.Equal(RegistryState.Built, provider.GetRequiredService<EventRegistry>().State);

                Interlocked.Exchange(ref GovernedProbeHandler.Calls, 0);
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new GovernedProbeEvent());
                Assert.Equal(1, GovernedProbeHandler.Calls);
            }
        }
    }

    // ── C02/C03: tenant modes, unknown tenants and context version matrix ──

    private static async Task<(ServiceProvider Provider, DeadLetterMessage Record)> BuildModeReplayAsync(
        Action<LocalEventDispatchRegistrationBuilder> declare,
        Action<IServiceCollection> configure)
    {
        var provider = LocalEventTestComposition.Build(
            declare: declare,
            configure: services =>
            {
                configure(services);
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);
        return (provider, record);
    }

    [Fact]
    public async Task Unknown_tenant_replay_is_refused_and_never_falls_back_to_host()
    {
        var state = new CurrentTenantProbeState();
        var (provider, record) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                var tenants = new FlakyTenantProvider();
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(new HardeningCurrentTenant("caller-tenant"));
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton(state);
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
            });

        await using (provider)
        {
            state.Provider = provider;
            var withTenant = record with
            {
                TenantId = "tenant-unknown",
                LocalContextVersion = LocalEventExecutionContextSnapshot.CurrentContextVersion,
            };
            await provider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(withTenant, CancellationToken.None);

            var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(withTenant, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("UnknownTenant", result.ProblemCode);
            Assert.Equal(0, state.Calls);
            var fresh = await provider.GetRequiredService<IDeadLetterStore>()
                .GetByIdAsync(withTenant.MessageId, CancellationToken.None);
            Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        }
    }

    [Fact]
    public async Task Tenant_provider_returning_a_different_id_is_refused()
    {
        var state = new CurrentTenantProbeState();
        var (provider, record) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                var tenants = new FlakyTenantProvider();
                tenants.AddWithDifferentId("tenant-by-name", "tenant-other-id");
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(new HardeningCurrentTenant("caller-tenant"));
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton(state);
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
            });

        await using (provider)
        {
            state.Provider = provider;
            var withTenant = record with
            {
                TenantId = "tenant-by-name",
                LocalContextVersion = LocalEventExecutionContextSnapshot.CurrentContextVersion,
            };
            await provider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(withTenant, CancellationToken.None);

            var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(withTenant, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("TenantIdMismatch", result.ProblemCode);
            Assert.Equal(0, state.Calls);
        }
    }

    [Fact]
    public async Task Tenant_mode_matrix_rejects_host_only_with_tenant_and_required_without_one()
    {
        // HostOnly + tenant -> refused.
        var (hostOnlyProvider, record) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions { TenantMode = LocalEventTenantMode.HostOnly }),
            configure: services => services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>());

        await using (hostOnlyProvider)
        {
            var withTenant = record with
            {
                TenantId = "tenant-a",
                LocalContextVersion = LocalEventExecutionContextSnapshot.CurrentContextVersion,
            };
            await hostOnlyProvider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(withTenant, CancellationToken.None);

            var result = await hostOnlyProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(withTenant, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("TenantModeHostOnly", result.ProblemCode);
        }

        // Required + no tenant -> refused.
        var (requiredProvider, requiredRecord) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions { TenantMode = LocalEventTenantMode.Required }),
            configure: services => services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>());

        await using (requiredProvider)
        {
            var result = await requiredProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(requiredRecord, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("TenantModeRequired", result.ProblemCode);
        }
    }

    [Fact]
    public async Task Unknown_context_version_is_refused_while_recognized_history_host_clears_the_caller_tenant()
    {
        var state = new CurrentTenantProbeState();

        // Unknown context version -> refused.
        var (unknownProvider, unknownRecord) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(new HardeningCurrentTenant("caller-tenant"));
                services.AddSingleton(state);
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
            });

        await using (unknownProvider)
        {
            state.Provider = unknownProvider;
            var future = unknownRecord with { LocalContextVersion = 99 };
            await unknownProvider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(future, CancellationToken.None);

            var result = await unknownProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(future, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("UnknownContextVersion", result.ProblemCode);
            Assert.Equal(0, state.Calls);
        }

        // Recognized legacy host row: executes as explicit host and clears the caller tenant.
        var (legacyProvider, legacyRecord) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions { AllowHistoricalHostContext = true }),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(new HardeningCurrentTenant("caller-tenant"));
                services.AddSingleton(state);
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
            });

        await using (legacyProvider)
        {
            state.Provider = legacyProvider;
            state.Calls = 0;
            var legacy = legacyRecord with { LocalContextVersion = null };
            await legacyProvider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(legacy, CancellationToken.None);

            var result = await legacyProvider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(legacy, CancellationToken.None);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, state.Calls);
            Assert.Null(state.ObservedTenantId);
            // The caller frame is restored after the attempt.
            Assert.Equal("caller-tenant", state.CurrentTenant!.Id);
        }
    }

    [Fact]
    public async Task Legacy_rows_without_the_explicit_migration_policy_are_refused_by_default()
    {
        var (provider, record) = await BuildModeReplayAsync(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services => services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>());

        await using (provider)
        {
            var legacy = record with { LocalContextVersion = null };
            await provider.GetRequiredService<IDeadLetterStore>().EnqueueAsync(legacy, CancellationToken.None);

            var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
                .ExecuteAsync(legacy, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("HistoricalContextNotRecognized", result.ProblemCode);
        }
    }

    [Fact]
    public async Task Channel_consumer_restores_the_published_tenant_and_correlation_for_handlers()
    {
        var tenant = new HardeningCurrentTenant("channel-tenant-A");
        var accessor = new HardeningAuditContextAccessor("channel-correlation");
        var tenants = new FlakyTenantProvider();
        tenants.Add("channel-tenant-A");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
        services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
        services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(accessor);
        services.AddSingleton<CrestCreates.Accountability.Abstractions.Identity.IAuditIdentityGenerator,
            HardeningAuditIdentityGenerator>();
        services.AddScoped<HardeningTenantProbeHandler>();
        services.AddScoped<ILocalEventHandler<HardeningTenantProbeEvent>>(
            sp => sp.GetRequiredService<HardeningTenantProbeHandler>());
        services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<HardeningTenantProbeEvent>(
            replayable: true,
            codecFactory: () => LocalEventTestComposition.CodecFor<HardeningTenantProbeEvent>(),
            policy: new LocalEventPolicyOptions { ReplayKey = "hardening.tenant-probe" }));
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetServices<IHostedService>().OfType<BackgroundChannelLocalEventBusConsumer>().Single();
        await (consumer.StartAsync(CancellationToken.None));
        try
        {
            HardeningTenantProbeHandler.LastObservedTenantId = null;
            HardeningTenantProbeHandler.LastObservedCorrelationId = null;
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ILocalEventBus>()
                    .PublishAsync(new HardeningTenantProbeEvent());
            }

            var until = DateTime.UtcNow.AddSeconds(5);
            while (HardeningTenantProbeHandler.LastObservedTenantId is null && DateTime.UtcNow < until)
            {
                await Task.Delay(25);
            }

            Assert.Equal("channel-tenant-A", HardeningTenantProbeHandler.LastObservedTenantId);
            Assert.Equal("channel-correlation", HardeningTenantProbeHandler.LastObservedCorrelationId);
            // Consumer frame restored: the ambient tenant is unchanged after processing.
            Assert.Equal("channel-tenant-A", tenant.Id);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }
}
