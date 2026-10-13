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
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
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
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
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
    public async Task PublishAsync_DeclaredEvent_With_No_Handler_Is_A_NoOp()
    {
        using var provider = CreateServiceProvider(declare: builder => builder.Declare<TestLocalEvent>());
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid())));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishAsync_Undeclared_Event_Throws_Deterministic_Diagnostic()
    {
        using var provider = CreateServiceProvider(declare: builder => builder.Declare<TestLocalEvent>());
        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new BaseTestEvent()));

        var unknownEvent = Assert.IsType<UnknownLocalEventException>(exception);
        Assert.Equal(typeof(BaseTestEvent), unknownEvent.EventType);
    }

    [Fact]
    public async Task PublishAsync_BaseClass_Variable_Does_Not_Dispatch_Derived_Events()
    {
        var handler = new DerivedEventRecordingHandler();
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<BaseTestEvent>(),
            configure: services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<DerivedTestEvent>>(sp => sp.GetRequiredService<DerivedEventRecordingHandler>());
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        ILocalEvent derivedAsBase = new DerivedTestEvent();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(derivedAsBase));

        Assert.IsType<UnknownLocalEventException>(exception);
        Assert.False(handler.Handled);
    }

    [Fact]
    public async Task PublishAsync_Generic_And_NonGeneric_Entries_Share_The_Same_Registration()
    {
        var handler = new RecordingLocalEventHandler();
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<RecordingLocalEventHandler>());
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var domainEvent = new TestLocalEvent(Guid.NewGuid());

        await eventBus.PublishAsync<TestLocalEvent>(domainEvent);
        await eventBus.PublishAsync((ILocalEvent)domainEvent);

        Assert.Equal(2, handler.InvocationCount);
    }

    [Fact]
    public async Task PublishAsync_PreCanceled_Token_Does_Not_Invoke_Handler()
    {
        var handler = new RecordingLocalEventHandler();
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<RecordingLocalEventHandler>());
            });

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid()), canceled.Token));

        Assert.False(handler.Handled);
    }

    [Fact]
    public async Task PublishAsync_Should_Propagate_Handler_Exception_And_Stop_Later_Handlers()
    {
        var tailHandler = new TailRecordingHandler();
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
            {
                services.AddSingleton<ThrowingLocalEventHandler>();
                services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<ThrowingLocalEventHandler>());
                services.AddSingleton(tailHandler);
                services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<TailRecordingHandler>());
            });

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var domainEvent = new TestLocalEvent(Guid.NewGuid());

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(domainEvent));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.False(tailHandler.Handled);
    }

    [Fact]
    public async Task Dispatch_Should_Resolve_Handlers_From_The_Caller_Scope()
    {
        using var provider = CreateServiceProvider(
            declare: builder => builder.Declare<TestLocalEvent>(),
            configure: services =>
            {
                services.AddScoped<ScopedProbe>();
                services.AddScoped<ScopedProbeHandler>();
                services.AddScoped<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<ScopedProbeHandler>());
            });

        using var scope = provider.CreateScope();
        var eventBus = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
        var probe = scope.ServiceProvider.GetRequiredService<ScopedProbe>();

        await eventBus.PublishAsync(new TestLocalEvent(Guid.NewGuid()));

        var handler = scope.ServiceProvider.GetRequiredService<ScopedProbeHandler>();
        Assert.Same(probe, handler.ObservedProbe);

        using var otherScope = provider.CreateScope();
        var otherProbe = otherScope.ServiceProvider.GetRequiredService<ScopedProbe>();
        Assert.NotSame(probe, otherProbe);
    }

    [Fact]
    public async Task DomainEventPublisher_Should_Delegate_To_Local_Event_Bus()
    {
        var recordedBus = new RecordingLocalEventBus();
        using var provider = CreateServiceProvider(configure: services =>
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

    private static ServiceProvider CreateServiceProvider(
        Action<LocalEventDispatchRegistrationBuilder>? declare = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddCrestLocalEventDispatch(declare);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    internal class BaseTestEvent : DomainEvent
    {
    }

    internal sealed class DerivedTestEvent : BaseTestEvent
    {
    }

    internal sealed class ScopedProbe
    {
    }

    internal sealed class RecordingLocalEventHandler : ILocalEventHandler<TestLocalEvent>
    {
        public bool Handled { get; private set; }
        public int InvocationCount { get; private set; }
        public TestLocalEvent? ReceivedEvent { get; private set; }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            Handled = true;
            InvocationCount++;
            ReceivedEvent = @event;
            return Task.CompletedTask;
        }
    }

    internal sealed class ScopedProbeHandler : ILocalEventHandler<TestLocalEvent>
    {
        private readonly ScopedProbe _probe;

        public ScopedProbeHandler(ScopedProbe probe)
        {
            _probe = probe;
        }

        public ScopedProbe? ObservedProbe { get; private set; }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            ObservedProbe = _probe;
            return Task.CompletedTask;
        }
    }

    internal sealed class DerivedEventRecordingHandler : ILocalEventHandler<DerivedTestEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(DerivedTestEvent @event, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    internal sealed class FirstRecordingHandler : ILocalEventHandler<TestLocalEvent>
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

    internal sealed class SecondRecordingHandler : ILocalEventHandler<TestLocalEvent>
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

    internal sealed class ThrowingLocalEventHandler : ILocalEventHandler<TestLocalEvent>
    {
        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("boom");
        }
    }

    internal sealed class TailRecordingHandler : ILocalEventHandler<TestLocalEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(TestLocalEvent @event, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingLocalEventBus : ILocalEventBus
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
