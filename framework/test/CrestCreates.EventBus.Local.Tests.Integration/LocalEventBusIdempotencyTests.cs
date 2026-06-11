using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Local.Channel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.EventBus.Local.Tests.Integration;

public class LocalEventBusIdempotencyTests
{
    [Fact]
    public async Task PublishAsync_SameEventTwice_BothDispatched()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<IdempotentTestEvent>, IdempotentTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var testEvent = new IdempotentTestEvent();

        await eventBus.PublishAsync(testEvent);
        await eventBus.PublishAsync(testEvent);

        var handler = provider.GetRequiredService<ILocalEventHandler<IdempotentTestEvent>>()
            as IdempotentTestEventHandler;
        handler!.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task DLQStore_EnqueueSameMessageId_Twice_OnlyStoredOnce()
    {
        var store = new InMemoryDeadLetterStore(
            Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new IdempotentTestEvent(), typeof(IdempotentTestEvent));

        var msg1 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err1", null,
            System.DateTime.UtcNow, System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        var msg2 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err2", null,
            System.DateTime.UtcNow, System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);

        await store.EnqueueAsync(msg1, CancellationToken.None);
        await store.EnqueueAsync(msg2, CancellationToken.None);

        var count = await store.CountAsync(null, CancellationToken.None);
        count.Should().Be(1);
        var stored = await store.GetByIdAsync("same-id", CancellationToken.None);
        stored!.ErrorMessage.Should().Be("err2");
    }

    private sealed class IdempotentTestEvent : DomainEvent { }

    private sealed class IdempotentTestEventHandler : ILocalEventHandler<IdempotentTestEvent>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(IdempotentTestEvent @event, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
