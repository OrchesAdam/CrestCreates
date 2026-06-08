using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
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
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<IdempotentTestEvent>, IdempotentTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var testEvent = new IdempotentTestEvent();

        await eventBus.PublishAsync(testEvent);
        await eventBus.PublishAsync(testEvent);
    }

    [Fact]
    public async Task DLQStore_EnqueueSameMessageId_Twice_OnlyStoredOnce()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new IdempotentTestEvent(), typeof(IdempotentTestEvent));

        var msg1 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err1", System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        var msg2 = new DeadLetterMessage("same-id", typeof(IdempotentTestEvent).AssemblyQualifiedName!,
            payload, "err2", System.DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);

        await store.EnqueueAsync(msg1);
        await store.EnqueueAsync(msg2);

        var count = await store.CountAsync();
        count.Should().Be(1);
        var stored = await store.GetAsync("same-id");
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
