using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using CrestCreates.Generated;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

// ── S3 review R1 probe shapes (top-level for the generator) ──

internal sealed class ReviewProbeDisposableState
{
    public int Disposals;
}

internal sealed class ReviewProbeDisposable(ReviewProbeDisposableState state) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref state.Disposals);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Resolves a scoped disposable first and then throws, simulating a failing check factory.</summary>
internal sealed class ThrowingResolutionCheck : ILocalEventExecutionCheck
{
    public ThrowingResolutionCheck(ReviewProbeDisposable disposable)
        => throw new InvalidOperationException("probe check construction failure");

    public Task<LocalEventExecutionCheckResult> CheckAsync(
        LocalEventExecutionCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(LocalEventExecutionCheckResult.Pass);
}

internal sealed class ThrowingPushAuditAccessor : CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor
{
    private CrestCreates.Accountability.Abstractions.Context.AuditOperationContext? _current;

    public CrestCreates.Accountability.Abstractions.Context.AuditOperationContext? Current => _current;

    public IDisposable Push(CrestCreates.Accountability.Abstractions.Context.AuditOperationContext context)
        => throw new InvalidOperationException("probe audit push failure");
}

internal sealed class CancelThenThrowPushAccessor(CancellationTokenSource source)
    : CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor
{
    public CrestCreates.Accountability.Abstractions.Context.AuditOperationContext? Current => null;

    public IDisposable Push(CrestCreates.Accountability.Abstractions.Context.AuditOperationContext context)
    {
        source.Cancel();
        throw new OperationCanceledException(source.Token);
    }
}

internal sealed class NonCallerOcePushAccessor : CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor
{
    public CrestCreates.Accountability.Abstractions.Context.AuditOperationContext? Current => null;

    public IDisposable Push(CrestCreates.Accountability.Abstractions.Context.AuditOperationContext context)
        => throw new OperationCanceledException("probe non-caller cancellation");
}

internal sealed class ReviewGovernedProbeEvent : DomainEvent
{
}

internal sealed class ReviewGovernedProbeHandler : ILocalEventHandler<ReviewGovernedProbeEvent>
{
    public static int Calls;

    public Task HandleAsync(ReviewGovernedProbeEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

/// <summary>
/// #146 S3 review R1 regressions: tenant mode at every accept/execute boundary, frozen check-set
/// immutability, empty-tenant handling, generation for abstractions-only contract assemblies,
/// bounded readiness waiting, and release of owned scopes/frames on admission failures.
/// </summary>
public class LocalEventGovernanceReviewR1Tests
{
    public sealed record BoundaryProbe(string Name, LocalEventTenantMode TenantMode, string ExpectedCode, string? CallerTenant);

    public static IEnumerable<object[]> BoundaryMatrix()
    {
        yield return [new BoundaryProbe("Required-no-tenant", LocalEventTenantMode.Required, "TenantModeRequired", null)];
        yield return [new BoundaryProbe("HostOnly-with-tenant", LocalEventTenantMode.HostOnly, "TenantModeHostOnly", "tenant-b")];
        yield return [new BoundaryProbe("Optional-tenant-no-provider", LocalEventTenantMode.Optional, "MissingTenantProvider", "tenant-b")];
    }

    private static (ServiceProvider Provider, IServiceCollection Services) BuildSyncBoundaryProvider(BoundaryProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        if (probe.CallerTenant is not null)
        {
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
                new HardeningCurrentTenant(probe.CallerTenant));
        }

        services.AddCrestLocalEventDispatch(builder => builder.Declare<ModeProbeEvent>(
            policy: new LocalEventPolicyOptions { TenantMode = probe.TenantMode }));
        services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
        services.AddSingleton(new CurrentTenantProbeState());
        LocalEventJsonCodecContributions.AddCodecs(services);
        return (services.BuildServiceProvider(), services);
    }

    [Theory]
    [MemberData(nameof(BoundaryMatrix))]
    public async Task Tenant_mode_matrix_is_enforced_at_publish_and_direct_dispatch_boundaries(BoundaryProbe probe)
    {
        await using var provider = BuildSyncBoundaryProvider(probe).Provider;

        await using var scope = provider.CreateAsyncScope();
        var publishFailure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
        Assert.Equal(probe.ExpectedCode, publishFailure.ProblemCode);

        var dispatchFailure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>().DispatchAsync(new ModeProbeEvent()));
        Assert.Equal(probe.ExpectedCode, dispatchFailure.ProblemCode);

        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
    }

    [Theory]
    [MemberData(nameof(BoundaryMatrix))]
    public async Task Tenant_mode_matrix_is_enforced_at_the_channel_enqueue_boundary(BoundaryProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (probe.CallerTenant is not null)
        {
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
                new HardeningCurrentTenant(probe.CallerTenant));
        }

        services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<ModeProbeEvent>(
            replayable: true,
            codecFactory: () => LocalEventTestComposition.CodecFor<ModeProbeEvent>(),
            policy: new LocalEventPolicyOptions { TenantMode = probe.TenantMode }));
        services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
        services.AddSingleton(new CurrentTenantProbeState());
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
            Assert.Equal(probe.ExpectedCode, failure.ProblemCode);
        }

        // No enqueue side effect: a consumer started afterwards sees nothing to process.
        var consumer = provider.GetServices<IHostedService>().OfType<BackgroundChannelLocalEventBusConsumer>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(300);
            Assert.Equal(0, provider.GetRequiredService<CurrentTenantProbeState>().Calls);
            Assert.Equal(0, await provider.GetRequiredService<IDeadLetterStore>().CountAsync(null, CancellationToken.None));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Frozen_binding_keeps_its_check_set_when_the_caller_collection_is_mutated()
    {
        var checkList = new List<Type> { typeof(RejectingExecutionCheck) };
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<RejectingCheckEvent>(
                policy: new LocalEventPolicyOptions { ExecutionCheckTypes = checkList }),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<RejectingCheckEvent>, RejectingCheckHandler>();
                services.AddScoped<RejectingExecutionCheck>();
            });

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        Assert.True(registry.TryGet(typeof(RejectingCheckEvent), out var binding));
        Assert.Equal(1, binding.ExecutionCheckTypes.Count);

        // Mutating the caller's original collection after freeze must not remove the check.
        checkList.Clear();
        Assert.Equal(1, binding.ExecutionCheckTypes.Count);

        Interlocked.Exchange(ref RejectingCheckHandler.Calls, 0);
        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventDispatcher>().DispatchAsync(new RejectingCheckEvent()));
        Assert.Equal("ProbeCheckRejected", failure.ProblemCode);
        Assert.Equal(0, RejectingCheckHandler.Calls);
    }

    [Theory]
    [InlineData(LocalEventTenantMode.Optional)]
    [InlineData(LocalEventTenantMode.HostOnly)]
    [InlineData(LocalEventTenantMode.Required)]
    public async Task Damaged_empty_persisted_tenant_id_is_refused_instead_of_becoming_host(LocalEventTenantMode mode)
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions { TenantMode = mode }),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: string.Empty);
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidTenantId", result.ProblemCode);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Channel_work_item_with_empty_persisted_tenant_is_refused_with_a_diagnostic_row()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<ModeProbeEvent>(
            replayable: true,
            codecFactory: () => LocalEventTestComposition.CodecFor<ModeProbeEvent>(),
            policy: new LocalEventPolicyOptions { TenantMode = LocalEventTenantMode.Required }));
        services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
        services.AddSingleton(new CurrentTenantProbeState());
        await using var provider = services.BuildServiceProvider();

        // Bypass the producer boundary on purpose: a damaged work item with an empty tenant id.
        var queue = provider.GetRequiredService<ChannelLocalEventQueue>();
        var damagedWorkItem = new LocalEventWorkItem(
            new ModeProbeEvent(),
            LocalEventExecutionContextSnapshot.FromPersisted(1, string.Empty, null),
            new LocalEventAcceptedIdentity(LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)), 1, null, null));
        await queue.EnqueueAsync(damagedWorkItem, CancellationToken.None);

        var consumer = provider.GetServices<IHostedService>().OfType<BackgroundChannelLocalEventBusConsumer>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var store = provider.GetRequiredService<IDeadLetterStore>();
            var until = DateTime.UtcNow.AddSeconds(5);
            while (await store.CountAsync(null, CancellationToken.None) == 0 && DateTime.UtcNow < until)
            {
                await Task.Delay(25);
            }

            Assert.Equal(0, provider.GetRequiredService<CurrentTenantProbeState>().Calls);
            Assert.Equal(1, await store.CountAsync(null, CancellationToken.None));
            var recorded = (await store.GetPendingAsync(0, 10, CancellationToken.None)).Single();
            Assert.Contains("damaged", recorded.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    // ── Boundary recoverability: resolution, exact id and association capability ──

    private static (ServiceProvider Provider, IServiceCollection Services) BuildTenantBoundaryProvider(
        Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch(builder => builder.Declare<ModeProbeEvent>());
        services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
        services.AddSingleton(new CurrentTenantProbeState());
        configure(services);
        LocalEventJsonCodecContributions.AddCodecs(services);
        return (services.BuildServiceProvider(), services);
    }

    [Fact]
    public async Task Publish_boundary_resolves_the_recorded_tenant_with_exact_id()
    {
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-b");
        await using var provider = BuildTenantBoundaryProvider(services =>
        {
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
                new HardeningCurrentTenant("tenant-b"));
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
        }).Provider;

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent());
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
    }

    [Fact]
    public async Task Unknown_tenant_is_refused_at_publish_and_enqueue_boundaries()
    {
        await using (var provider = BuildTenantBoundaryProvider(services =>
                     {
                         services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
                             new HardeningCurrentTenant("tenant-unknown"));
                         services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(
                             new FlakyTenantProvider());
                     }).Provider)
        {
            await using var scope = provider.CreateAsyncScope();
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
            Assert.Equal("UnknownTenant", failure.ProblemCode);
            Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
            new HardeningCurrentTenant("tenant-unknown"));
        services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(new FlakyTenantProvider());
        services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<ModeProbeEvent>(
            replayable: true,
            codecFactory: () => LocalEventTestComposition.CodecFor<ModeProbeEvent>()));
        services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
        services.AddSingleton(new CurrentTenantProbeState());
        await using var channelProvider = services.BuildServiceProvider();

        await using (var scope = channelProvider.CreateAsyncScope())
        {
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
            Assert.Equal("UnknownTenant", failure.ProblemCode);
        }

        var consumer = channelProvider.GetServices<IHostedService>()
            .OfType<BackgroundChannelLocalEventBusConsumer>().Single();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(300);
            Assert.Equal(0, channelProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Tenant_provider_name_fallback_to_a_different_id_is_refused_at_publish()
    {
        var tenants = new FlakyTenantProvider();
        tenants.AddWithDifferentId("tenant-by-name", "tenant-other-id");
        await using var provider = BuildTenantBoundaryProvider(services =>
        {
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(
                new HardeningCurrentTenant("tenant-by-name"));
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
        }).Provider;

        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
        Assert.Equal("TenantIdMismatch", failure.ProblemCode);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
    }

    [Fact]
    public async Task Correlation_without_identity_generator_is_refused_at_publish()
    {
        await using var provider = BuildTenantBoundaryProvider(services =>
        {
            services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(new HardeningCurrentTenant());
            services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(
                new HardeningAuditContextAccessor("boundary-correlation"));
            // IAuditIdentityGenerator deliberately absent.
        }).Provider;

        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
            () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ModeProbeEvent()));
        Assert.Equal("MissingAuditFacility", failure.ProblemCode);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<CurrentTenantProbeState>().Calls);
    }

    // ── Activation + unwind combinations ──

    [Fact]
    public async Task Activation_failure_with_failed_frame_unwind_reports_release_failed()
    {
        var tenant = new HardeningCurrentTenant("caller-tenant-B", throwOnRestore: true);
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-a");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(
                    new ThrowingPushAuditAccessor());
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Identity.IAuditIdentityGenerator,
                    HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: "tenant-a",
            correlationId: "review-correlation");
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ContextRestoreFailed", result.ProblemCode);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        // The failed unwind is a release fact, not a footnote: Release must not claim Completed.
        Assert.Equal(LocalEventReleaseOutcome.Failed, result.Release);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Cancellation_during_activation_with_failed_unwind_keeps_the_original_cancellation()
    {
        using var cancel = new CancellationTokenSource();
        var tenant = new HardeningCurrentTenant("caller-tenant-B", throwOnRestore: true);
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-a");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(
                    new CancelThenThrowPushAccessor(cancel));
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Identity.IAuditIdentityGenerator,
                    HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: "tenant-a",
            correlationId: "review-correlation");
        await store.EnqueueAsync(record, CancellationToken.None);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ILocalDeadLetterReplayExecutor>().ExecuteAsync(record, cancel.Token));

        // The caller cancellation keeps its identity; the unwind failure rides on it as data.
        var cleanup = error.Data["LocalEventContextCleanupFailure"];
        Assert.NotNull(cleanup);
        Assert.Contains("probe tenant restore failure", cleanup!.ToString(), StringComparison.Ordinal);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Composed_but_undriven_governance_provider_rejects_within_a_bound_and_recovers_after_start()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestLocalEventDispatch(builder => builder.Declare<ReviewGovernedProbeEvent>(
            policy: new LocalEventPolicyOptions { Governance = LocalEventGovernance.Required }));
        services.AddSingleton<IEventDescriptorProvider>(new ProbeDescriptorProvider(new GeneratedEventDescriptor
        {
            Id = "evt_review_probe_v1",
            Name = "review.probe",
            Version = 1,
            State = DescriptorState.Active,
            PayloadType = typeof(ReviewGovernedProbeEvent),
            Scope = EventScope.Local,
        }));
        services.AddScoped<ILocalEventHandler<ReviewGovernedProbeEvent>, ReviewGovernedProbeHandler>();
        services.AddCrestLocalEventGovernance();
        await using var provider = services.BuildServiceProvider();

        Interlocked.Exchange(ref ReviewGovernedProbeHandler.Calls, 0);
        var stopwatch = Stopwatch.StartNew();
        await using (var scope = provider.CreateAsyncScope())
        {
            var failure = await Assert.ThrowsAsync<LocalEventAdmissionException>(
                () => scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ReviewGovernedProbeEvent()));
            Assert.Equal("GovernanceRegistryNotReady", failure.ProblemCode);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"bounded rejection took {stopwatch.Elapsed}");
        Assert.Equal(0, ReviewGovernedProbeHandler.Calls);

        // The same composition, now actually driven: the not-ready state was not cached.
        var coordinator = provider.GetServices<IHostedService>().OfType<CrestCreates.Metadata.Bootstrap.BootstrapCoordinator>().Single();
        await coordinator.StartAsync(CancellationToken.None);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILocalEventBus>().PublishAsync(new ReviewGovernedProbeEvent());
        }

        Assert.Equal(1, ReviewGovernedProbeHandler.Calls);
    }

    [Fact]
    public async Task Replay_admission_exception_releases_scope_and_context_frame_without_touching_budget()
    {
        var disposalState = new ReviewProbeDisposableState();
        var tenant = new HardeningCurrentTenant("caller-tenant-B");
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-a");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(
                replayable: true,
                policy: new LocalEventPolicyOptions { ExecutionCheckTypes = new[] { typeof(ThrowingResolutionCheck) } }),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton(disposalState);
                services.AddScoped<ReviewProbeDisposable>();
                services.AddScoped<ThrowingResolutionCheck>();
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: "tenant-a");
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("AdmissionFailed", result.ProblemCode);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
        // The scoped disposable resolved by the failing check factory was released exactly once and
        // the already-activated tenant frame was restored to the caller.
        Assert.Equal(1, disposalState.Disposals);
        Assert.Equal("caller-tenant-B", tenant.Id);
    }

    [Fact]
    public async Task Partial_context_activation_failure_unwinds_the_tenant_frame_and_disposes_the_scope()
    {
        var tenant = new HardeningCurrentTenant("caller-tenant-B");
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-a");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(
                    new ThrowingPushAuditAccessor());
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Identity.IAuditIdentityGenerator,
                    HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: "tenant-a",
            correlationId: "review-correlation");
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ContextRestoreFailed", result.ProblemCode);
        // The tenant frame entered before the failing audit push was unwound in Activate itself.
        Assert.Equal("caller-tenant-B", tenant.Id);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Non_caller_cancellation_with_failed_unwind_reports_release_failed()
    {
        var tenant = new HardeningCurrentTenant("caller-tenant-B", throwOnRestore: true);
        var tenants = new FlakyTenantProvider();
        tenants.Add("tenant-a");

        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ModeProbeEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ICurrentTenant>(tenant);
                services.AddSingleton<CrestCreates.MultiTenancy.Abstract.ITenantProvider>(tenants);
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(
                    new NonCallerOcePushAccessor());
                services.AddSingleton<CrestCreates.Accountability.Abstractions.Identity.IAuditIdentityGenerator,
                    HardeningAuditIdentityGenerator>();
                services.AddScoped<ILocalEventHandler<ModeProbeEvent>, ModeProbeHandler>();
                services.AddSingleton(new CurrentTenantProbeState());
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var record = LocalEventTestComposition.CreateModernMessage<ModeProbeEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ModeProbeEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ModeProbeEvent)),
            tenantId: "tenant-a",
            correlationId: "review-correlation");
        await store.EnqueueAsync(record, CancellationToken.None);

        // The caller token is NOT cancelled: the OCE comes from another source, so this is an
        // ordinary failure result — and the already-failed frame unwind must reach Release.
        var result = await provider.GetRequiredService<ILocalDeadLetterReplayExecutor>()
            .ExecuteAsync(record, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ContextRestoreFailed", result.ProblemCode);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        Assert.Equal(LocalEventReleaseOutcome.Failed, result.Release);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }
}
