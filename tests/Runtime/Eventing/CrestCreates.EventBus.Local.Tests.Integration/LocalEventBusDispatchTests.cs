using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusDispatchTests
{
    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerSucceeds_NoDLQEntry()
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
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var exception = await Record.ExceptionAsync(
            () => eventBus.PublishAsync(new ReplayableDispatchEvent { Message = "ok" }));

        exception.Should().BeNull();
        handler.InvocationCount.Should().Be(1);
        (await store.CountAsync(null, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerFails_RecordedWithTypedIdentity_AndExceptionPropagated()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<ReplayableFailingEvent>(replayable: true),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<ReplayableFailingEvent>, ReplayableFailingHandler>();
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var probeEvent = new ReplayableFailingEvent { Message = "recorded" };

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(probeEvent));

        exception.Should().BeOfType<InvalidOperationException>();
        var messages = await store.GetPendingAsync(0, 10, CancellationToken.None);
        messages.Should().HaveCount(1);

        var message = messages[0];
        message.EventName.Should().Be(typeof(ReplayableFailingEvent).FullName);
        message.EventVersion.Should().Be(1);
        message.LocalReplayKey.Should().Be(LocalEventIdentity.BuildLocalReplayKey(typeof(ReplayableFailingEvent)));
        message.PayloadTypeFullName.Should().Contain(nameof(ReplayableFailingEvent));
        message.Status.Should().Be(DeadLetterStatus.Pending);
        message.RetryCount.Should().Be(0);

        var decoded = JsonSerializer.Deserialize(
            message.Payload, LocalEventTestComposition.CodecFor<ReplayableFailingEvent>());
        decoded!.Message.Should().Be("recorded");
    }

    [Fact]
    public async Task PublishAsync_DispatchOnlyFailure_IsNotRecorded_AndOriginalExceptionPropagates()
    {
        var control = new HandlerControl { ShouldFail = true };
        using var provider = LocalEventTestComposition.Build(
            declare: builder => builder.Declare<DispatchOnlyEvent>(),
            configure: services =>
            {
                services.AddSingleton(control);
                services.AddScoped<ILocalEventHandler<DispatchOnlyEvent>, DispatchOnlyFailingHandler>();
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new DispatchOnlyEvent()));

        exception.Should().BeOfType<InvalidOperationException>();
        (await store.CountAsync(null, CancellationToken.None)).Should().Be(0);
    }

    internal sealed class DispatchOnlyFailingHandler : ILocalEventHandler<DispatchOnlyEvent>
    {
        public Task HandleAsync(DispatchOnlyEvent @event, CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("dispatch-only failure"));
    }
}
