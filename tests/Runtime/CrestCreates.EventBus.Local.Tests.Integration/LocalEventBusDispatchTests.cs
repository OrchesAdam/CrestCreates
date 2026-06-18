using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusDispatchTests
{
    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerSucceeds_NoDLQEntry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<SuccessTestEvent>, SuccessTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new SuccessTestEvent()));

        exception.Should().BeNull();
        var dlqCount = await store.CountAsync(null, CancellationToken.None);
        dlqCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WithDLQ_HandlerFails_EventInDLQAndExceptionPropagated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<AlwaysFailEvent>, AlwaysFailEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new AlwaysFailEvent()));

        exception.Should().NotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
        var dlqCount = await store.CountAsync(null, CancellationToken.None);
        dlqCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WithoutDLQ_HandlerFails_ExceptionPropagated_NoDLQ()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<AlwaysFailEvent>, AlwaysFailEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(new AlwaysFailEvent()));

        exception.Should().NotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
    }

    private sealed class SuccessTestEvent : DomainEvent { }

    private sealed class SuccessTestEventHandler : ILocalEventHandler<SuccessTestEvent>
    {
        public bool WasCalled { get; private set; }

        public Task HandleAsync(SuccessTestEvent @event, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailEvent : DomainEvent { }

    private sealed class AlwaysFailEventHandler : ILocalEventHandler<AlwaysFailEvent>
    {
        public Task HandleAsync(AlwaysFailEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Always fails");
        }
    }
}
