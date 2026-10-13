using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalDeadLetterQueueTests
{
    [Fact]
    public async Task EnqueueFailingEvent_HandlerThrows_EventEntersDLQ()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildFailingProvider(control);
        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var exception = await Record.ExceptionAsync(
            () => eventBus.PublishAsync(new ReplayableFailingEvent { Message = "boom" }));

        exception.Should().NotBeNull();
        var messages = await store.GetPendingAsync(0, 10, CancellationToken.None);
        messages.Should().HaveCount(1);
        messages[0].PayloadTypeFullName.Should().Contain(nameof(ReplayableFailingEvent));
        messages[0].Status.Should().Be(DeadLetterStatus.Pending);
        messages[0].RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetrySucceeds_DLQRetry_HandlerSucceeds_MarkedRetried()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildFailingProvider(control);
        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        await Record.ExceptionAsync(() => eventBus.PublishAsync(new ReplayableFailingEvent { Message = "retry me" }));
        var message = (await store.GetPendingAsync(0, 1, CancellationToken.None))[0];

        control.ShouldFail = false;
        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeTrue();
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
        updated.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetryExhausted_ReachesMaxRetries_StatusArchived()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildFailingProvider(control);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            retryCount: 2,
            maxRetries: 3);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Archived);
        updated.RetryCount.Should().Be(3);
        (await store.CountAsync(null, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task ManualRetry_RetryAsync_RetriesSpecificMessage()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildFailingProvider(control);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var message = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            retryCount: 1);
        await store.EnqueueAsync(message, CancellationToken.None);

        control.ShouldFail = false;
        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public async Task RetryAll_RetriesAllPendingMessages()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = BuildFailingProvider(control);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        for (var i = 0; i < 3; i++)
        {
            await store.EnqueueAsync(
                LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
                    payload,
                    LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent))),
                CancellationToken.None);
        }

        control.ShouldFail = false;
        var results = await manager.RetryAllAsync();

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(result => result.Success.Should().BeTrue());
    }

    [Fact]
    public async Task FullQueue_DoesNotBlock_SameKeyStateUpdates()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configureDeadLetter: options => options.MaxQueueSize = 2,
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableFailingEvent());
        var tracked = LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
            payload,
            LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
            messageId: "tracked");
        await store.EnqueueAsync(tracked, CancellationToken.None);
        await store.EnqueueAsync(
            LocalEventTestComposition.CreateModernMessage<ReplayableFailingEvent>(
                payload,
                LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)),
                messageId: "filler"),
            CancellationToken.None);

        control.ShouldFail = false;
        var result = await manager.RetryAsync("tracked");

        result.Success.Should().BeTrue();
        var updated = await store.GetByIdAsync("tracked", CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task ClearByEventType_RemovesMatchingMessages()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent());

        await store.EnqueueAsync(LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
            payload, messageId: "1"), CancellationToken.None);
        await store.EnqueueAsync(LocalEventTestComposition.CreateLegacyMessage<ReplayableFailingEvent>(
            payload, messageId: "2"), CancellationToken.None);

        var type1Results = await store.GetByEventNameAsync(
            typeof(ReplayableDispatchEvent).FullName!, 0, 100, CancellationToken.None);
        type1Results.Count.Should().Be(1);
        var type2Results = await store.GetByEventNameAsync(
            typeof(ReplayableFailingEvent).FullName!, 0, 100, CancellationToken.None);
        type2Results.Count.Should().Be(1);
    }

    [Fact]
    public async Task ListWithFilter_SupportsPagingAndTypeFilter()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent());

        for (var i = 0; i < 5; i++)
        {
            await store.EnqueueAsync(
                LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                    payload, messageId: i.ToString()) with
                {
                    FailedAt = DateTime.UtcNow.AddMinutes(-i),
                },
                CancellationToken.None);
        }

        var page1 = await store.GetPendingAsync(0, 2, CancellationToken.None);
        var page2 = await store.GetPendingAsync(2, 2, CancellationToken.None);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1[0].FailedAt.Should().BeBefore(page2[0].FailedAt);
    }

    [Fact]
    public async Task ConcurrentEnqueue_MultipleThreads_DataConsistent()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent());
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var id = i.ToString();
            tasks.Add(Task.Run(async () =>
            {
                await store.EnqueueAsync(
                    LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                        payload, messageId: id),
                    CancellationToken.None);
            }));
        }

        await Task.WhenAll(tasks);

        (await store.CountAsync(null, CancellationToken.None)).Should().Be(100);
    }

    [Fact]
    public async Task DeadLetterStats_ReturnsCorrectCounts()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent());

        await store.EnqueueAsync(LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
            payload, messageId: "1"), CancellationToken.None);
        await store.EnqueueAsync(
            LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                payload, messageId: "2") with { Status = DeadLetterStatus.Retried },
            CancellationToken.None);
        await store.EnqueueAsync(
            LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                payload, messageId: "3", retryCount: 3) with { Status = DeadLetterStatus.Archived },
            CancellationToken.None);

        var total = await store.CountAsync(null, CancellationToken.None);
        var pending = await store.CountAsync(DeadLetterStatus.Pending, CancellationToken.None);
        var retried = await store.CountAsync(DeadLetterStatus.Retried, CancellationToken.None);
        var archived = await store.CountAsync(DeadLetterStatus.Archived, CancellationToken.None);

        total.Should().Be(3);
        pending.Should().Be(1);
        retried.Should().Be(1);
        archived.Should().Be(1);
    }

    [Fact]
    public async Task MaxQueueSizeProtection_EnqueueRespectsLimit()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
            configureDeadLetter: options => options.MaxQueueSize = 5);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var payload = LocalEventTestComposition.SerializeWithCodec(new ReplayableDispatchEvent());

        for (var i = 0; i < 5; i++)
        {
            await store.EnqueueAsync(
                LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                    payload, messageId: i.ToString()),
                CancellationToken.None);
        }

        (await store.CountAsync(null, CancellationToken.None)).Should().Be(5);

        var action = async () => await store.EnqueueAsync(
            LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
                payload, messageId: "overflow"),
            CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Dead letter queue is full*");
    }

    private static ServiceProvider BuildFailingProvider(HandlerControl control)
        => LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });
}
