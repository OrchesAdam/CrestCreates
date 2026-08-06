using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;
using Xunit;

namespace CrestCreates.EventBus.Tests;

public sealed class DomainEventMainlineTests
{
    [Fact]
    public async Task StronglyTyped_DomainEvent_Path_Should_Publish_In_Order_And_Clear()
    {
        var entity = new TestEntity(Guid.NewGuid());
        var first = new TestDomainEvent(entity.Id);
        var second = new TestDomainEvent(entity.Id);
        entity.AddDomainEvent(first);
        entity.AddDomainEvent(second);

        IHasDomainEvents eventSource = entity;
        var eventBus = new RecordingLocalEventBus();
        var publisher = new DomainEventPublisher(eventBus);

        foreach (var domainEvent in eventSource.DomainEvents)
        {
            await publisher.PublishAsync(domainEvent);
        }

        eventSource.ClearDomainEvents();

        Assert.Equal(new ILocalEvent[] { first, second }, eventBus.PublishedEvents);
        Assert.Empty(eventSource.DomainEvents);
    }

    private sealed class RecordingLocalEventBus : ILocalEventBus
    {
        public List<ILocalEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : ILocalEvent
        {
            return PublishAsync((ILocalEvent)@event, cancellationToken);
        }
    }
}
