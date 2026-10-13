using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusIdempotencyTests
{
    [Fact]
    public async Task PublishAsync_SameEventTwice_BothDispatched()
    {
        var handler = new ReplayableDispatchHandler();
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<ReplayableDispatchEvent>>(sp => handler);
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var testEvent = new ReplayableDispatchEvent { Message = "twice" };

        await eventBus.PublishAsync(testEvent);
        await eventBus.PublishAsync(testEvent);

        handler.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task DLQStore_EnqueueSameMessageId_Twice_OnlyStoredOnce()
    {
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableDispatchEvent>(replayable: true));
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var payload = LocalEventTestComposition.SerializeWithCodec(
            new ReplayableDispatchEvent { Message = "same-id" });

        var msg1 = LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
            payload, messageId: "same-id");
        var msg2 = LocalEventTestComposition.CreateLegacyMessage<ReplayableDispatchEvent>(
            payload, messageId: "same-id") with { ErrorMessage = "err2" };

        await store.EnqueueAsync(msg1, CancellationToken.None);
        await store.EnqueueAsync(msg2, CancellationToken.None);

        (await store.CountAsync(null, CancellationToken.None)).Should().Be(1);
        var stored = await store.GetByIdAsync("same-id", CancellationToken.None);
        stored!.ErrorMessage.Should().Be("err2");
    }
}
