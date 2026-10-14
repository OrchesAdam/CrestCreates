using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

// ── S2 lifecycle probes ──

internal sealed class CountingAsyncDisposable : IAsyncDisposable
{
    public static int DisposeCount;
    public static bool ThrowOnDispose;

    public static void Reset(bool throwOnDispose = false)
    {
        Interlocked.Exchange(ref DisposeCount, 0);
        ThrowOnDispose = throwOnDispose;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCount);
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException("probe disposal failure");
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class ReleaseProbeHandler(CountingAsyncDisposable probe) : ILocalEventHandler<ReplayableDispatchEvent>
{
    public Task HandleAsync(ReplayableDispatchEvent @event, CancellationToken cancellationToken = default)
    {
        // Touch the scoped probe so the handler holds a real scope-bound dependency; disposal happens
        // when the execution scope is released.
        _ = probe.GetHashCode();
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingCtorHandler : ILocalEventHandler<ReplayableDispatchEvent>
{
    public ThrowingCtorHandler() => throw new InvalidOperationException("probe construction failure");

    public Task HandleAsync(ReplayableDispatchEvent @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class CountingDispatchHandler : ILocalEventHandler<ReplayableDispatchEvent>
{
    public static int Calls;

    public Task HandleAsync(ReplayableDispatchEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Task.CompletedTask;
    }
}

internal sealed class AsyncDisposableFailingHandler(CountingAsyncDisposable probe, HandlerControl control)
    : ILocalEventHandler<ReplayableFailingEvent>
{
    public Task HandleAsync(ReplayableFailingEvent @event, CancellationToken cancellationToken = default)
    {
        _ = probe;
        var call = control.RegisterCall();
        return control.ShouldFail
            ? Task.FromException(new InvalidOperationException($"probe handler failure #{call}"))
            : Task.CompletedTask;
    }
}

/// <summary>Delegating store that commits MarkRetrying first and then throws (unconfirmed-write probe).</summary>
internal sealed class CommitThenThrowMarkStore(InMemoryDeadLetterStore inner) : IDeadLetterStore
{
    public static bool ThrowOnMarkRetrying;

    public Task EnqueueAsync(DeadLetterMessage message, CancellationToken ct) => inner.EnqueueAsync(message, ct);

    public Task<IReadOnlyList<DeadLetterMessage>> GetPendingAsync(int skip, int take, CancellationToken ct)
        => inner.GetPendingAsync(skip, take, ct);

    public Task<DeadLetterMessage?> GetByIdAsync(string messageId, CancellationToken ct) => inner.GetByIdAsync(messageId, ct);

    public async Task MarkRetryingAsync(string messageId, CancellationToken ct)
    {
        await inner.MarkRetryingAsync(messageId, ct);
        if (ThrowOnMarkRetrying)
        {
            throw new InvalidOperationException("probe MarkRetrying failure after commit");
        }
    }

    public Task MarkRetriedAsync(string messageId, CancellationToken ct) => inner.MarkRetriedAsync(messageId, ct);

    public Task MarkArchivedAsync(string messageId, CancellationToken ct) => inner.MarkArchivedAsync(messageId, ct);

    public Task<int> CountAsync(DeadLetterStatus? status, CancellationToken ct) => inner.CountAsync(status, ct);

    public Task<IReadOnlyList<DeadLetterMessage>> GetByEventNameAsync(string eventName, int skip, int take, CancellationToken ct)
        => inner.GetByEventNameAsync(eventName, skip, take, ct);
}

/// <summary>
/// #146 S2 lifecycle regressions: contributor-id conflicts are not swallowed, prepare-phase failures
/// leave state and budget untouched, and settlement/release failures are reported independently with
/// exactly-once disposal and no duplicate dead letters.
/// </summary>
public class LocalEventHardeningStateMachineTests
{
    private sealed class ManualContributor(string contributorId, Action<LocalEventDispatchRegistrationBuilder> register)
        : ILocalEventRegistrationContributor
    {
        public string ContributorId => contributorId;

        public void Register(LocalEventDispatchRegistrationBuilder builder) => register(builder);
    }

    // ── H04: same ContributorId is not a skip condition ──

    [Fact]
    public void Conflicting_metadata_under_one_contributor_id_is_not_swallowed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalEventRegistrationContributor>(new ManualContributor("Shared.Id", builder =>
            builder.Declare<ReplayableDispatchEvent>(replayable: false)));
        services.AddSingleton<ILocalEventRegistrationContributor>(new ManualContributor("Shared.Id", builder =>
            builder.Declare<ReplayableDispatchEvent>(replayable: true)));
        services.AddCrestLocalEventDispatch();
        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<LocalEventDispatchConfigurationException>(
            () => provider.GetRequiredService<ILocalEventDispatchRegistry>());
        Assert.Contains(failure.Violations, v => v.Contains("declared both as replayable"));
    }

    [Fact]
    public void Identical_declarations_under_one_contributor_id_are_idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalEventRegistrationContributor>(new ManualContributor("Shared.Id", builder =>
            builder.Declare<ReplayableDispatchEvent>(replayable: false)));
        services.AddSingleton<ILocalEventRegistrationContributor>(new ManualContributor("Shared.Id", builder =>
            builder.Declare<ReplayableDispatchEvent>(replayable: false)));
        services.AddCrestLocalEventDispatch();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ILocalEventDispatchRegistry>();
        Assert.Single(registry.Bindings);
    }

    // ── P02: prepare failures leave state and budget untouched ──

    [Fact]
    public async Task Replay_with_zero_handlers_is_refused_without_state_or_budget_change()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await manager.RetryAsync(record.MessageId);

        Assert.False(result.Success);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        Assert.Equal("ZeroHandlers", result.ProblemCode);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    [Fact]
    public async Task Replay_with_throwing_handler_constructor_is_refused_without_state_or_budget_change()
    {
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
            configure: services =>
            {
                services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, ThrowingCtorHandler>();
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await manager.RetryAsync(record.MessageId);

        Assert.False(result.Success);
        Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
        Assert.Equal("PrepareFailed", result.ProblemCode);
        var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
        Assert.Equal(0, fresh.RetryCount);
    }

    // ── R02: unconfirmed MarkRetrying write ──

    [Fact]
    public async Task MarkRetrying_commit_then_throw_reports_unknown_without_executing_or_rolling_back()
    {
        CommitThenThrowMarkStore.ThrowOnMarkRetrying = true;
        try
        {
            await using var provider = LocalEventTestComposition.Build(
                declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
                configure: services =>
                {
                    services.RemoveAll<IDeadLetterStore>();
                    services.AddSingleton<CommitThenThrowMarkStore>(sp => new CommitThenThrowMarkStore(new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()))));
                    services.AddSingleton<IDeadLetterStore>(sp => sp.GetRequiredService<CommitThenThrowMarkStore>());
                    services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, CountingDispatchHandler>();
                });

            var store = provider.GetRequiredService<IDeadLetterStore>();
            var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
            var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
                LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
                LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
            await store.EnqueueAsync(record, CancellationToken.None);
            Interlocked.Exchange(ref CountingDispatchHandler.Calls, 0);

            var result = await manager.RetryAsync(record.MessageId);

            Assert.False(result.Success);
            Assert.Equal(LocalEventExecutionOutcome.NotStarted, result.Execution);
            Assert.Equal(LocalEventStateWriteOutcome.Unknown, result.StateWrite);
            Assert.Equal("MarkRetryingUnconfirmed", result.ProblemCode);
            Assert.Equal(0, CountingDispatchHandler.Calls);
            // The committed-but-unconfirmed write is preserved (not rolled back to Pending).
            var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
            Assert.Equal(DeadLetterStatus.Retrying, fresh!.Status);
        }
        finally
        {
            CommitThenThrowMarkStore.ThrowOnMarkRetrying = false;
        }
    }

    // ── P03/R03: async-only disposal exactly once; success + release failure ──

    [Fact]
    public async Task Async_only_disposable_is_released_exactly_once_after_handler_success()
    {
        CountingAsyncDisposable.Reset();
        await using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
            configure: services =>
            {
                services.AddScoped<CountingAsyncDisposable>();
                services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, ReleaseProbeHandler>();
            });

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
        var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
            LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
        await store.EnqueueAsync(record, CancellationToken.None);

        var result = await manager.RetryAsync(record.MessageId);

        Assert.True(result.Success);
        Assert.Equal(LocalEventExecutionOutcome.Completed, result.Execution);
        Assert.Equal(LocalEventReleaseOutcome.Completed, result.Release);
        Assert.Equal(1, CountingAsyncDisposable.DisposeCount);
    }

    [Fact]
    public async Task Handler_success_with_release_failure_reports_completed_and_no_duplicate_dead_letter()
    {
        CountingAsyncDisposable.Reset(throwOnDispose: true);
        try
        {
            await using var provider = LocalEventTestComposition.Build(
                declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
                configure: services =>
                {
                    services.AddScoped<CountingAsyncDisposable>();
                    services.AddScoped<ILocalEventHandler<ReplayableDispatchEvent>, ReleaseProbeHandler>();
                });

            var store = provider.GetRequiredService<IDeadLetterStore>();
            var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
            var record = LocalEventTestComposition.CreateModernMessage<ReplayableDispatchEvent>(
                LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent()),
                LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableDispatchEvent)));
            await store.EnqueueAsync(record, CancellationToken.None);

            var result = await manager.RetryAsync(record.MessageId);

            // The completed execution fact is preserved; the release failure is reported independently
            // and no business failure re-queue or second dead letter is produced.
            Assert.False(result.Success);
            Assert.Equal(LocalEventExecutionOutcome.Completed, result.Execution);
            Assert.Equal(LocalEventStateWriteOutcome.Confirmed, result.StateWrite);
            Assert.Equal(LocalEventReleaseOutcome.Failed, result.Release);
            Assert.Equal("ReleaseFailed", result.ProblemCode);
            Assert.Equal(1, await store.CountAsync(null, CancellationToken.None));
            var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
            Assert.Equal(DeadLetterStatus.Retried, fresh!.Status);
            Assert.Equal(0, fresh.RetryCount);
        }
        finally
        {
            CountingAsyncDisposable.Reset();
        }
    }

    // ── R04: handler failure with release failure — single budget step, independent facts ──

    [Fact]
    public async Task Handler_failure_with_release_failure_reports_both_with_single_budget_step()
    {
        CountingAsyncDisposable.Reset(throwOnDispose: true);
        var control = new HandlerControl { ShouldFail = true };
        try
        {
            await using var provider = LocalEventTestComposition.Build(
                declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
                configure: services =>
                {
                    services.AddSingleton(control);
                    services.AddScoped<CountingAsyncDisposable>();
                    services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, AsyncDisposableFailingHandler>();
                });

            var store = provider.GetRequiredService<IDeadLetterStore>();
            var manager = provider.GetRequiredService<ILocalDeadLetterManager>();
            var record = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
                LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent()),
                LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));
            await store.EnqueueAsync(record, CancellationToken.None);

            var result = await manager.RetryAsync(record.MessageId);

            Assert.False(result.Success);
            Assert.Equal(LocalEventExecutionOutcome.Failed, result.Execution);
            Assert.Equal(LocalEventStateWriteOutcome.Confirmed, result.StateWrite);
            Assert.Equal(LocalEventReleaseOutcome.Failed, result.Release);
            Assert.True(result.AnyHandlerInvoked);
            Assert.Equal("HandlerFailed", result.ProblemCode);
            Assert.Equal(1, control.Calls);
            var fresh = await store.GetByIdAsync(record.MessageId, CancellationToken.None);
            Assert.Equal(DeadLetterStatus.Pending, fresh!.Status);
            Assert.Equal(1, fresh.RetryCount);
        }
        finally
        {
            CountingAsyncDisposable.Reset();
        }
    }
}
