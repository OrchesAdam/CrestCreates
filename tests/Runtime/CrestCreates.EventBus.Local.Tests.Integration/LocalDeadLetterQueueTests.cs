using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
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
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var eventBus = provider.GetRequiredService<ILocalEventBus>();
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var testEvent = new FailingTestEvent();

        var exception = await Record.ExceptionAsync(() => eventBus.PublishAsync(testEvent));

        exception.Should().NotBeNull();
        var messages = await store.GetPendingAsync(0, 10, CancellationToken.None);
        messages.Should().HaveCount(1);
        messages[0].PayloadTypeFullName.Should().Contain(nameof(FailingTestEvent));
        messages[0].Status.Should().Be(DeadLetterStatus.Pending);
        messages[0].RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetrySucceeds_DLQRetry_HandlerSucceeds_MarkedRetried()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            Guid.NewGuid().ToString("N"),
            typeof(RetryTestEvent).Name!,
            1,
            null, null, null,
            EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload,
            "Test failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            0,
            3,
            DeadLetterStatus.Pending);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeTrue();
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Fact]
    public async Task RetryExhausted_ReachesMaxRetries_StatusArchived()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<FailingTestEvent>, FailingTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new FailingTestEvent(), typeof(FailingTestEvent));
        var message = new DeadLetterMessage(
            Guid.NewGuid().ToString("N"),
            typeof(FailingTestEvent).Name!,
            1,
            null, null, null,
            EventScope.Local,
            typeof(FailingTestEvent).AssemblyQualifiedName!,
            payload,
            "Test failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            2,
            3,
            DeadLetterStatus.Pending);
        await store.EnqueueAsync(message, CancellationToken.None);

        var result = await manager.RetryAsync(message.MessageId);

        result.Success.Should().BeFalse();
        var updated = await store.GetByIdAsync(message.MessageId, CancellationToken.None);
        updated!.Status.Should().Be(DeadLetterStatus.Archived);
        updated.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task ManualRetry_RetryAsync_RetriesSpecificMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var message = new DeadLetterMessage(
            Guid.NewGuid().ToString("N"),
            typeof(RetryTestEvent).Name!,
            1,
            null, null, null,
            EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload,
            "Test failure",
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            1,
            3,
            DeadLetterStatus.Pending);
        await store.EnqueueAsync(message, CancellationToken.None);

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
        services.AddSingleton<IEventValidator, PassThroughEventValidator>();
        services.Configure<LocalDeadLetterOptions>(_ => { });
        services.AddSingleton<IDeadLetterStore, InMemoryDeadLetterStore>();
        services.AddScoped<ILocalDeadLetterManager, DefaultLocalDeadLetterManager>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<ILocalEventHandler<RetryTestEvent>, RetryTestEventHandler>();
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IDeadLetterStore>();
        var manager = provider.GetRequiredService<ILocalDeadLetterManager>();

        for (int i = 0; i < 3; i++)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
            var message = new DeadLetterMessage(
                Guid.NewGuid().ToString("N"),
                typeof(RetryTestEvent).Name!,
                1,
                null, null, null,
                EventScope.Local,
                typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload,
                $"Failure {i}",
                null,
                DateTime.UtcNow,
                DateTime.UtcNow,
                0,
                3,
                DeadLetterStatus.Pending);
            await store.EnqueueAsync(message, CancellationToken.None);
        }

        var results = await manager.RetryAllAsync();

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task ClearByEventType_RemovesMatchingMessages()
    {
        var store = new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        var msg1 = new DeadLetterMessage("1", typeof(RetryTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "err", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        var msg2 = new DeadLetterMessage("2", typeof(FailingTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(FailingTestEvent).AssemblyQualifiedName!,
            payload, "err", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried);
        await store.EnqueueAsync(msg1, CancellationToken.None);
        await store.EnqueueAsync(msg2, CancellationToken.None);

        var type1Results = await store.GetByEventNameAsync(
            typeof(RetryTestEvent).Name!, 0, 100, CancellationToken.None);
        type1Results.Count.Should().Be(1);
        var type2Results = await store.GetByEventNameAsync(
            typeof(FailingTestEvent).Name!, 0, 100, CancellationToken.None);
        type2Results.Count.Should().Be(1);
    }

    [Fact]
    public async Task ListWithFilter_SupportsPagingAndTypeFilter()
    {
        var store = new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 5; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).Name!, 1,
                null, null, null, EventScope.Local,
                typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, $"err {i}", null,
                DateTime.UtcNow.AddMinutes(-i), DateTime.UtcNow.AddMinutes(-i),
                0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg, CancellationToken.None);
        }

        var page1 = await store.GetPendingAsync(0, 2, CancellationToken.None);
        var page2 = await store.GetPendingAsync(2, 2, CancellationToken.None);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1[0].FailedAt.Should().BeBefore(page2[0].FailedAt);
    }

    [Fact]
    public async Task IdempotencyWithDLQ_DuplicateEvent_NoDuplicateProcessing()
    {
        var store = new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CountingTestEvent(), typeof(CountingTestEvent));

        var msg = new DeadLetterMessage("dup-1", typeof(CountingTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(CountingTestEvent).AssemblyQualifiedName!,
            payload, "err", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        await store.EnqueueAsync(msg, CancellationToken.None);
        await store.EnqueueAsync(msg, CancellationToken.None);

        var all = await store.GetPendingAsync(0, 10, CancellationToken.None);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentEnqueue_MultipleThreads_DataConsistent()
    {
        var store = new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i.ToString();
            tasks.Add(Task.Run(async () =>
            {
                var msg = new DeadLetterMessage(id, typeof(RetryTestEvent).Name!, 1,
                    null, null, null, EventScope.Local,
                    typeof(RetryTestEvent).AssemblyQualifiedName!,
                    payload, "err", null,
                    DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
                await store.EnqueueAsync(msg, CancellationToken.None);
            }));
        }
        await Task.WhenAll(tasks);

        var count = await store.CountAsync(null, CancellationToken.None);
        count.Should().Be(100);
    }

    [Fact]
    public async Task DeadLetterStats_ReturnsCorrectCounts()
    {
        var store = new InMemoryDeadLetterStore(Microsoft.Extensions.Options.Options.Create(new LocalDeadLetterOptions()));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        await store.EnqueueAsync(new DeadLetterMessage("1", typeof(RetryTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending), CancellationToken.None);
        await store.EnqueueAsync(new DeadLetterMessage("2", typeof(RetryTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Retried), CancellationToken.None);
        await store.EnqueueAsync(new DeadLetterMessage("3", typeof(RetryTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "e", null,
            DateTime.UtcNow, DateTime.UtcNow, 3, 3, DeadLetterStatus.Archived), CancellationToken.None);

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
        var options = Microsoft.Extensions.Options.Options.Create(
            new LocalDeadLetterOptions { MaxQueueSize = 500 });
        var store = new InMemoryDeadLetterStore(options);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RetryTestEvent(), typeof(RetryTestEvent));

        for (int i = 0; i < 500; i++)
        {
            var msg = new DeadLetterMessage(i.ToString(), typeof(RetryTestEvent).Name!, 1,
                null, null, null, EventScope.Local,
                typeof(RetryTestEvent).AssemblyQualifiedName!,
                payload, "err", null,
                DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
            await store.EnqueueAsync(msg, CancellationToken.None);
        }

        var count = await store.CountAsync(null, CancellationToken.None);
        count.Should().Be(500);

        var overflow = new DeadLetterMessage("overflow", typeof(RetryTestEvent).Name!, 1,
            null, null, null, EventScope.Local,
            typeof(RetryTestEvent).AssemblyQualifiedName!,
            payload, "err", null,
            DateTime.UtcNow, DateTime.UtcNow, 0, 3, DeadLetterStatus.Pending);
        var action = async () => await store.EnqueueAsync(overflow, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Dead letter queue is full*");
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
