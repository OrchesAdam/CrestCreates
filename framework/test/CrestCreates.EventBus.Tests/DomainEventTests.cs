using System;using System.Collections.Generic;using System.Threading.Tasks;using Xunit;using Moq;using FluentAssertions;using MediatR;using CrestCreates.Domain.DomainEvents;using CrestCreates.Domain.Entities;using CrestCreates.EventBus.Local;using CrestCreates.EventBus.Tests.Events;using CrestCreates.EventBus.Tests.Handlers;

namespace CrestCreates.EventBus.Tests
{
    public class DomainEventTests
    {
        [Fact]
        public void DomainEvent_Should_Have_OccurredOn_Set()
        {
            // Arrange & Act
            var domainEvent = new TestDomainEvent(Guid.NewGuid());

            // Assert
            domainEvent.OccurredOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Entity_Should_Add_DomainEvent()
        {
            // Arrange
            var entity = new TestEntity(Guid.NewGuid());
            var domainEvent = new TestDomainEvent(Guid.NewGuid());

            // Act
            entity.AddDomainEvent(domainEvent);

            // Assert
            entity.DomainEvents.Should().Contain(domainEvent);
            entity.DomainEvents.Count.Should().Be(1);
        }

        [Fact]
        public void Entity_Should_Remove_DomainEvent()
        {
            // Arrange
            var entity = new TestEntity(Guid.NewGuid());
            var domainEvent = new TestDomainEvent(Guid.NewGuid());
            entity.AddDomainEvent(domainEvent);

            // Act
            entity.RemoveDomainEvent(domainEvent);

            // Assert
            entity.DomainEvents.Should().NotContain(domainEvent);
            entity.DomainEvents.Count.Should().Be(0);
        }

        [Fact]
        public void Entity_Should_Clear_DomainEvents()
        {
            // Arrange
            var entity = new TestEntity(Guid.NewGuid());
            var domainEvent1 = new TestDomainEvent(Guid.NewGuid());
            var domainEvent2 = new TestDomainEvent(Guid.NewGuid());
            entity.AddDomainEvent(domainEvent1);
            entity.AddDomainEvent(domainEvent2);

            // Act
            entity.ClearDomainEvents();

            // Assert
            entity.DomainEvents.Should().BeEmpty();
        }

        [Fact]
        public async Task DomainEventPublisher_Should_Publish_Event()
        {
            // Arrange
            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var domainEvent = new TestDomainEvent(Guid.NewGuid());

            // Act
            await domainEventPublisher.PublishAsync(domainEvent);

            // Assert
            mediatorMock.Verify(m => m.Publish(domainEvent, default), Times.Once);
        }

        [Fact]
        public async Task DomainEventPublisher_Should_Publish_Typed_Event()
        {
            // Arrange
            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);
            var domainEvent = new TestDomainEvent(Guid.NewGuid());

            // Act
            await domainEventPublisher.PublishAsync(domainEvent);

            // Assert
            mediatorMock.Verify(m => m.Publish(domainEvent, default), Times.Once);
        }
    }

    // 测试实体
    public class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id)
        {
            Id = id;
        }

        public string Name { get; set; } = string.Empty;
    }
}
