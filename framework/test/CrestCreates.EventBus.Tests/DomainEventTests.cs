using System;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;
using CrestCreates.EventBus.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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
            Assert.InRange((DateTime.UtcNow - domainEvent.OccurredOn).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
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
            Assert.Contains(domainEvent, entity.DomainEvents);
            Assert.Equal(1, entity.DomainEvents.Count);
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
            Assert.DoesNotContain(domainEvent, entity.DomainEvents);
            Assert.Empty(entity.DomainEvents);
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
            Assert.Empty(entity.DomainEvents);
        }

        [Fact]
        public void Entity_Should_Implement_HasDomainEvents_Contract()
        {
            var entity = new TestEntity(Guid.NewGuid());

            Assert.IsAssignableFrom<IHasDomainEvents>(entity);
            entity.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));

            var hasDomainEvents = (IHasDomainEvents)entity;

            Assert.Single(hasDomainEvents.DomainEvents);

            hasDomainEvents.ClearDomainEvents();

            Assert.Empty(hasDomainEvents.DomainEvents);
        }

        [Fact]
        public async Task DomainEventPublisher_Should_Delegate_To_Local_Bus()
        {
            var handler = new TestDomainEventHandler();
            using var provider = CreateServiceProvider(services =>
            {
                services.AddSingleton(handler);
                services.AddSingleton<ILocalEventHandler<TestLocalEvent>>(sp => sp.GetRequiredService<TestDomainEventHandler>());
            });

            var publisher = provider.GetRequiredService<IDomainEventPublisher>();
            var domainEvent = new TestLocalEvent(Guid.NewGuid());

            await publisher.PublishAsync(domainEvent);

            Assert.True(handler.WasCalled);
            Assert.Same(domainEvent, handler.ReceivedEvent);
        }

        private static ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
            services.AddSingleton<ILocalEventBus, DefaultLocalEventBus>();
            services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
            configure?.Invoke(services);
            return services.BuildServiceProvider();
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
