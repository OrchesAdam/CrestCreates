using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions.UnitOfWorkBase;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;
using Xunit;

namespace CrestCreates.EventBus.Tests;

public class UnitOfWorkEventTests
{
    [Fact]
    public async Task SaveChangesWithEventsAsync_Should_Publish_DomainEvents_And_Clear_Them()
    {
        var recordedBus = new RecordingLocalEventBus();
        var domainEventPublisher = new DomainEventPublisher(recordedBus);
        var unitOfWork = new TestUnitOfWork(domainEventPublisher);

        var entity = new TestEntity(Guid.NewGuid());
        var firstEvent = new TestDomainEvent(entity.Id);
        var secondEvent = new TestDomainEvent(Guid.NewGuid());
        entity.AddDomainEvent(firstEvent);
        entity.AddDomainEvent(secondEvent);

        var result = await unitOfWork.CommitAsync([entity]);

        Assert.Equal(1, result);
        Assert.Equal(new ILocalEvent[] { firstEvent, secondEvent }, recordedBus.PublishedEvents);
        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public async Task SaveChangesWithEventsAsync_Should_Skip_Publishing_When_No_DomainEvents_Exist()
    {
        var recordedBus = new RecordingLocalEventBus();
        var domainEventPublisher = new DomainEventPublisher(recordedBus);
        var unitOfWork = new TestUnitOfWork(domainEventPublisher);

        var entity = new TestEntity(Guid.NewGuid());

        var result = await unitOfWork.CommitAsync([entity]);

        Assert.Equal(1, result);
        Assert.Empty(recordedBus.PublishedEvents);
        Assert.Empty(entity.DomainEvents);
    }

    private sealed class TestUnitOfWork : UnitOfWorkWithEvents
    {
        public TestUnitOfWork(IDomainEventPublisher domainEventPublisher)
            : base(domainEventPublisher)
        {
        }

        public Task<int> CommitAsync(IEnumerable<TestEntity> entities, CancellationToken cancellationToken = default)
        {
            return SaveChangesWithEventsAsync<TestEntity, Guid>(entities, cancellationToken);
        }

        public override Task BeginTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public override Task CommitTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public override Task RollbackTransactionAsync()
        {
            return Task.CompletedTask;
        }

        public override Task<int> SaveChangesAsync()
        {
            return Task.FromResult(1);
        }

        public override void Dispose()
        {
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

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : ILocalEvent
        {
            return PublishAsync((ILocalEvent)@event, cancellationToken);
        }
    }

    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id)
        {
            Id = id;
        }
    }
}
