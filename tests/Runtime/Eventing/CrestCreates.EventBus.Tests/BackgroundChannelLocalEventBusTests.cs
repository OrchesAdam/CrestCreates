using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using CrestCreates.EventBus.Tests.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Tests;

public class BackgroundChannelLocalEventBusTests
{
    [Fact]
    public async Task PublishAsync_Should_Enqueue_And_Background_Consumer_Should_Dispatch()
    {
        var handler = new ChannelRecordingHandler();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(handler);
            services.AddSingleton<ILocalEventHandler<TestChannelLocalEvent>>(sp => sp.GetRequiredService<ChannelRecordingHandler>());
        });

        var consumer = provider.GetRequiredService<BackgroundChannelLocalEventBusConsumer>();
        await consumer.StartAsync(CancellationToken.None);

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var domainEvent = new TestChannelLocalEvent(Guid.NewGuid().ToString("N"));

        await eventBus.PublishAsync(domainEvent);

        var observed = await handler.ReceivedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(domainEvent, observed);

        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_Undeclared_Event_Throws_Before_Enqueue()
    {
        using var provider = CreateServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var exception = await Record.ExceptionAsync(
            () => eventBus.PublishAsync(new LocalEventBusTests.BaseTestEvent()));

        Assert.IsType<UnknownLocalEventException>(exception);
    }

    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalChannelEventDispatch(builder => builder.Declare<TestChannelLocalEvent>());
        services.AddSingleton<BackgroundChannelLocalEventBusConsumer>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    internal sealed class ChannelRecordingHandler : ILocalEventHandler<TestChannelLocalEvent>
    {
        public TaskCompletionSource<TestChannelLocalEvent> ReceivedEvent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(TestChannelLocalEvent @event, CancellationToken cancellationToken = default)
        {
            ReceivedEvent.TrySetResult(@event);
            return Task.CompletedTask;
        }
    }
}
