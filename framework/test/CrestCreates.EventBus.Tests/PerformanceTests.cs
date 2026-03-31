using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using MediatR;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Domain.Entities;
using CrestCreates.EventBus.Local;
using CrestCreates.EventBus.Tests.Events;

namespace CrestCreates.EventBus.Tests
{
    public class PerformanceTests
    {
        [Fact]
        public void Reflection_vs_GeneratedCode_Performance_Comparison()
        {
            // Arrange
            var entity = new TestEntity(Guid.NewGuid());
            var domainEvent = new TestDomainEvent(entity.Id);
            entity.AddDomainEvent(domainEvent);

            var mediatorMock = new Mock<IMediator>();
            var domainEventPublisher = new DomainEventPublisher(mediatorMock.Object);

            // Act & Assert
            var reflectionTime = MeasureReflectionTime(entity, domainEventPublisher);
            var generatedCodeTime = MeasureGeneratedCodeTime(entity, domainEventPublisher);

            // 验证生成的代码性能优于反射
            generatedCodeTime.Should().BeLessThan(reflectionTime);
        }

        private long MeasureReflectionTime(Entity<Guid> entity, DomainEventPublisher publisher)
        {
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 10000; i++)
            {
                // 使用反射获取领域事件
                var entityType = entity.GetType();
                var domainEventsProperty = entityType.GetProperty("DomainEvents");
                var clearDomainEventsMethod = entityType.GetMethod("ClearDomainEvents");

                if (domainEventsProperty != null && clearDomainEventsMethod != null)
                {
                    var domainEvents = domainEventsProperty.GetValue(entity) as IReadOnlyCollection<IDomainEvent>;
                    if (domainEvents != null)
                    {
                        foreach (var domainEvent in domainEvents)
                        {
                            publisher.PublishAsync(domainEvent).Wait();
                        }
                        clearDomainEventsMethod.Invoke(entity, null);
                    }
                }

                // 重新添加事件以进行下一次测试
                entity.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));
            }

            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private long MeasureGeneratedCodeTime(Entity<Guid> entity, DomainEventPublisher publisher)
        {
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 10000; i++)
            {
                // 使用生成的代码获取领域事件
                foreach (var domainEvent in entity.DomainEvents)
                {
                    publisher.PublishAsync(domainEvent).Wait();
                }
                entity.ClearDomainEvents();

                // 重新添加事件以进行下一次测试
                entity.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));
            }

            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }
    }
}
