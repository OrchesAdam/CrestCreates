using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Tests;

public class LocalEventBusTests
{
    [Fact]
    public async Task PublishAsync_Should_Dispatch_To_Single_Handler()
    {
        var handler = new RecordingLocalEventHandler();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(handler);
            services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<RecordingLocalEventHandler>());
        });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var domainEvent = new TestLocalEvent(Guid.NewGuid());

        await eventBus.PublishAsync(domainEvent);

        Assert.True(handler.Handled);
        Assert.Same(domainEvent, handler.ReceivedEvent);
    }

    [Fact]
    public async Task PublishAsync_Should_Dispatch_To_Multiple_Handlers_In_Registration_Order()
    {
        var callOrder = new List<string>();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(new FirstRecordingHandler(callOrder));
            services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<FirstRecordingHandler>());
            services.AddSingleton(new SecondRecordingHandler(callOrder));
            services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<SecondRecordingHandler>());
        });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        await eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid()));

        Assert.Equal(new[] { "first", "second" }, callOrder);
    }

    [Fact]
    public async Task PublishAsync_Should_Not_Throw_When_No_Handler_Is_Registered()
    {
        using var provider = CreateServiceProvider();
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var act = () => eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid()));

        var exception = await Record.ExceptionAsync(act);
        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishAsync_Should_Propagate_Handler_Exception_And_Stop_Later_Handlers()
    {
        var tailHandler = new TailRecordingHandler();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton<ThrowingLocalEventHandler>();
            services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<ThrowingLocalEventHandler>());
            services.AddSingleton(tailHandler);
            services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<TailRecordingHandler>());
        });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var domainEvent = new TestLocalEvent(Guid.NewGuid());

        var act = () => eventBus.PublishAsync(domainEvent);

        var exception = await Record.ExceptionAsync(act);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.False(tailHandler.Handled);
    }

    [Fact]
    public async Task DomainEventPublisher_Should_Delegate_To_Local_Event_Bus()
    {
        var recordedBus = new RecordingLocalEventBus();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton<ILocalEventBus>(recordedBus);
            services.AddSingleton<DomainEventPublisher>();
        });

        var publisher = provider.GetRequiredService<DomainEventPublisher>();
        var domainEvent = new TestLocalEvent(Guid.NewGuid());

        await publisher.PublishAsync(domainEvent);

        Assert.Single(recordedBus.PublishedEvents);
        Assert.Same(domainEvent, recordedBus.PublishedEvents[0]);
    }

    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddSingleton<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddSingleton<ILocalEventBus, DefaultLocalEventBus>();
        services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingLocalEventHandler : ILocalEventHandler<TestLocalEvent>
    {
        public bool Handled { get; private set; }
        public TestLocalEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            Handled = true;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    private sealed class FirstRecordingHandler : ILocalEventHandler<TestLocalEvent>
    {
        private readonly ICollection<string> _callOrder;

        public FirstRecordingHandler(ICollection<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            _callOrder.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondRecordingHandler : ILocalEventHandler<TestLocalEvent>
    {
        private readonly ICollection<string> _callOrder;

        public SecondRecordingHandler(ICollection<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            _callOrder.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingLocalEventHandler : ILocalEventHandler<TestLocalEvent>
    {
        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class TailRecordingHandler : ILocalEventHandler<TestLocalEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLocalEventBus : ILocalEventBus
    {
        public List<ILocalEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : ILocalEvent
        {
            return PublishAsync((ILocalEvent)@event, cancellationToken);
        }
    }
}
