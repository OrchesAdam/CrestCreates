using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalDeadLetterReplaySemanticsTests
{
    [Fact]
    public async Task UnknownReplayKey_IsRejected_AndMessageStaysPending()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, "SomeAssembly:Some.UnknownEvent");
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no registered local event matches replay key");
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Pending);
        updated.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task UnknownVersion_IsRejected()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<VersionedReplayEvent>(replayable: true, version: 2));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new VersionedReplayEvent());
        var message = LocalEventTestComposition.CreateModernMessage<VersionedReplayEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(VersionedReplayEvent)),
            eventVersion: 1);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no registered local event matches replay key");
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Pending);
    }

    [Fact]
    public async Task LegacyMessage_WithoutReplayKey_ResolvesByPayloadIdentity()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var legacyMessage = LocalEventTestComposition.CreateLegacyMessage<ReplayableFailingEvent>(
            payload,
            payloadTypeFullName: $"{typeof(ReplayableFailingEvent).FullName}, LegacyAssemblyName, Version=9.9.9.9, Culture=neutral, PublicKeyToken=null");
        await store.EnqueueAsync(legacyMessage, CancellationToken.None);

        control.ShouldFail = false;
        var result = await manager.RetryAsync(legacyMessage.MessageId);

        result.Success.Should().BeTrue();
        (await store.GetByIdAsync(legacyMessage.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task LegacyAlias_ResolvesToRegisteredEvent()
    {
        var handler = new LegacyAliasHandler();
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<LegacyAliasedReplayEvent>(
                replayable: true,
                legacyIdentities: new[] { new LocalEventLegacyIdentity("CrestCreates.Legacy.GoneEvent", 1) }),
            configure: services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<LegacyAliasedReplayEvent>>(sp => handler);
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new LegacyAliasedReplayEvent { Message = "old" });
        var legacyMessage = LocalEventTestComposition.CreateLegacyMessage<LegacyAliasedReplayEvent>(
            payload,
            payloadTypeFullName: "CrestCreates.Legacy.GoneEvent, OldAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        await store.EnqueueAsync(legacyMessage, CancellationToken.None);

        var result = await manager.RetryAsync(legacyMessage.MessageId);

        result.Success.Should().BeTrue();
        handler.InvocationCount.Should().Be(1);
        handler.LastMessage.Should().Be("old");
    }

    [Fact]
    public async Task LegacyMessage_ForDispatchOnlyEvent_IsRejected()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<DispatchOnlyEvent>());
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = "{}"u8.ToArray();
        var message = LocalEventTestComposition.CreateLegacyMessage<DispatchOnlyEvent>(payload);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("dispatch-only");
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Pending);
    }

    [Fact]
    public async Task ReplayableEvent_WithoutAnyHandler_IsRefusedBeforeStateTransition()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableOrphanEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableOrphanEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableOrphanEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableOrphanEvent)));
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("resolved no handlers");
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Pending);
        updated.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task CorruptPayload_Is_Refused_Before_Any_State_Transition_And_Keeps_Budget()
    {
        // #146 R01 revised the #126 semantics: decode/identity/prepare complete before MarkRetrying,
        // so a corrupt payload is refused without consuming the retry budget and never archives.
        var control = new HandlerControl();
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            "not-json"u8.ToArray(),
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            maxRetries: 1);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ProblemCode.Should().Be("PayloadDecode");
        result.Execution.Should().Be(LocalEventExecutionOutcome.NotStarted);
        control.Calls.Should().Be(0);
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Pending);
        updated.RetryCount.Should().Be(0);
        (await store.CountAsync(null, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task PendingWithExhaustedBudget_IsArchived_WithoutExecutingHandler()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            retryCount: 3,
            maxRetries: 3);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("archived without executing");
        control.Calls.Should().Be(0);
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Archived);
    }

    [Fact]
    public async Task InvalidBudget_IsRejected_WithoutStateChange()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            maxRetries: 0);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("invalid persisted MaxRetries");
        (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
            .Should().Be(DeadLetterStatus.Pending);
    }

    [Fact]
    public async Task TerminalAndRetryingStatuses_Reject_OrdinaryRetry()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        foreach (var status in new[] { DeadLetterStatus.Retried, DeadLetterStatus.Archived, DeadLetterStatus.Retrying })
        {
            var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
                payload,
                LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
                messageId: status.ToString()) with { Status = status };
            await store.EnqueueAsync(message, CancellationToken.None);

            var result = await manager.RetryAsync(message.MessageId);

            result.Success.Should().BeFalse();
            (await store.GetByIdAsync(message.MessageId, CancellationToken.None))!.Status
                .Should().Be(status);
        }
    }

    [Fact]
    public async Task PreCanceledToken_LeavesStateUntouched()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));
        await store.EnqueueAsync(message, CancellationToken.None);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RetryAsync(message.MessageId, canceled.Token));

        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Pending);
        updated.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task CooperativeCancellation_DuringAttempt_RestoresPending_AndPropagates()
    {
        var control = new HandlerControl();

        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<CancellableReplayEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<CancellableReplayEvent>, CancellableReplayHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new CancellableReplayEvent());
        var message = LocalEventTestComposition.CreateModernMessage<CancellableReplayEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(CancellableReplayEvent)));
        await store.EnqueueAsync(message, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var retryTask = manager.RetryAsync(message.MessageId, cts.Token);
        await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retryTask);

        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Pending);
        updated.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task BackgroundService_RetriesPendingMessages_ThroughTheSameCore()
    {
        var control = new HandlerControl { ShouldFail = false };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configureDeadLetter: options => options.RetryIntervalSeconds = 1,
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload, LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));
        await store.EnqueueAsync(message, CancellationToken.None);

        var service = new LocalDeadLetterBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILocalDeadLetterReplayExecutor>(),
            provider.GetRequiredService<IOptions<LocalDeadLetterOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalDeadLetterBackgroundService>>());

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            DeadLetterMessage? current = null;
            while (DateTime.UtcNow < deadline)
            {
                current = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
                if (current!.Status == DeadLetterStatus.Retried)
                {
                    break;
                }

                await Task.Delay(100);
            }

            current!.Status.Should().Be(DeadLetterStatus.Retried);
            control.Calls.Should().Be(1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    internal sealed class LegacyAliasHandler : ILocalEventHandler<LegacyAliasedReplayEvent>
    {
        public int InvocationCount { get; private set; }

        public string? LastMessage { get; private set; }

        public Task HandleAsync(LegacyAliasedReplayEvent @event, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastMessage = @event.Message;
            return Task.CompletedTask;
        }
    }
}
