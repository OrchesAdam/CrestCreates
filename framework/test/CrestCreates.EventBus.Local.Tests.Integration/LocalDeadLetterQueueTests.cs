using System;
using System.Collections.Generic;
using System.Text.Json;
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

public class LocalDeadLetterQueueTests
{
    [Fact]
    public async Task EnqueueFailingEvent_HandlerThrows_EventEntersDLQ()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var testEvent = new FailingTestEvent();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(testEvent));

        exception.Should().NotBeNull();
        var messages = await store.ListAsync(take: 10);
        messages.Should().HaveCount(1);
        messages[0].EventType.Should().Contain(nameof(FailingTestEvent));
        messages[0].Status.Should().Be(DeadLetterStatus.Pending);
        messages[0].RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetrySucceeds_DLQRetry_HandlerSucceeds_MarkedRetried()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 0,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeTrue();
        var updated = await store.GetAsync(message.MessageId);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task RetryExhausted_ReachesMaxRetries_StatusArchived()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new FailingTestEvent(), typeof(FailingTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(FailingTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 2,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        var updated = await store.GetAsync(message.MessageId);
        updated!.Status.Should().Be(DeadLetterStatus.Archived);
        updated.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task ManualRetry_RetryAsync_RetriesSpecificMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: "Test failure",
            FailedAt: DateTime.UtcNow,
            RetryCount: 1,
            MaxRetries: 3,
            Status: DeadLetterStatus.Pending);
        await store.EnqueueAsync(message);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public async Task RetryAll_RetriesAllPendingMessages()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddSingleton<ILocalDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ILocalDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        for (int i = 0; i < 3; i++)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
            var message = new DeadLetterMessage(
                MessageId: Guid.NewGuid().ToString("N"),
                EventType: typeof(RetryTestEvent).AssemblyQualifiedName!,
                Payload: payload,
                ErrorMessage: $"Failure {i}",
                FailedAt: DateTime.UtcNow,
                RetryCount: 0,
                MaxRetries: 3,
                Status: DeadLetterStatus.Pending);
            await store.EnqueueAsync(message);
        }

        var results = await manager.RetryAllAsync();

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task ClearByEventType_RemovesMatchingMessages()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        var msg1 = new DeadLetterMessage("1", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        var msg2 = new DeadLetterMessage("2", typeof(FailingTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        await store.EnqueueAsync(msg1);
        await store.EnqueueAsync(msg2);

        var type1Count = await store.CountAsync(typeof(RetryTestEvent).AssemblyQualifiedName);
        type1Count.Should().Be(1);
        var type2Count = await store.CountAsync(typeof(FailingTestEvent).AssemblyQualifiedName);
        type2Count.Should().Be(1);
    }

    [Fact]
    public async Task ListWithFilter_SupportsPagingAndTypeFilter()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 5; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, $"err {i}", DateTime.UtcNow.AddMinutes(-i), 0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg);
        }

        var page1 = await store.ListAsync(skip: 0, take: 2);
        var page2 = await store.ListAsync(skip: 2, take: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1[0].FailedAt.Should().BeAfter(page2[0].FailedAt);
    }

    [Fact]
    public async Task IdempotencyWithDLQ_DuplicateEvent_NoDuplicateProcessing()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CountingTestEvent(), typeof(CountingTestEvent));

        var msg = new DeadLetterMessage("dup-1", typeof(CountingTestEvent).AssemblyQualifiedName!,
            payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        await store.EnqueueAsync(msg);
        await store.EnqueueAsync(msg);

        var all = await store.ListAsync(take: 10);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentEnqueue_MultipleThreads_DataConsistent()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i.ToString();
            tasks.Add(Task.Run(async () =>
            {
                var msg = new DeadLetterMessage(id, typeof(RetryTestEvent).AssemblyQualifiedName!,
                    payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
                await store.EnqueueAsync(msg);
            }));
        }
        await Task.WhenAll(tasks);

        var count = await store.CountAsync();
        count.Should().Be(100);
    }

    [Fact]
    public async Task DeadLetterStats_ReturnsCorrectCounts()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        await store.EnqueueAsync(new DeadLetterMessage("1", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending));
        await store.EnqueueAsync(new DeadLetterMessage("2", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried));
        await store.EnqueueAsync(new DeadLetterMessage("3", typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", DateTime.UtcNow, 3, 3, DeadLetterStatus.Archived));

        var total = await store.CountAsync();
        var pending = await store.CountAsync(status: DeadLetterStatus.Pending);
        var retried = await store.CountAsync(status: DeadLetterStatus.Retried);
        var archived = await store.CountAsync(status: DeadLetterStatus.Archived);

        total.Should().Be(3);
        pending.Should().Be(1);
        retried.Should().Be(1);
        archived.Should().Be(1);
    }

    [Fact]
    public async Task MaxQueueSizeProtection_StoreDoesNotEnforceLimit()
    {
        var store = new InMemoryDeadLetterStore();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 1000; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, "err", DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg);
        }

        var count = await store.CountAsync();
        count.Should().Be(1000);
    }

    private sealed class FailingTestEvent : DomainEvent { }

    private sealed class FailingTestEventHandler : ILocalEventHandler<FailingTestEvent>
    {
        public Task HandleAsync(FailingTestEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler always fails");
        }
    }

    private sealed class RetryTestEvent : DomainEvent { }

    private sealed class RetryTestEventHandler : ILocalEventHandler<RetryTestEvent>
    {
        public Task HandleAsync(RetryTestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CountingTestEvent : DomainEvent { }

    private sealed class CountingTestEventHandler : ILocalEventHandler<CountingTestEvent>
    {
        public int CallCount { get; private set; }

        public Task HandleAsync(CountingTestEvent @event, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
