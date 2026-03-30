using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CrestCreates.EventBus.Tests.Events;

namespace CrestCreates.EventBus.Tests.Handlers
{
    public class TestDomainEventHandler : INotificationHandler<TestDomainEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public TestDomainEvent? ReceivedEvent { get; private set; } = null;

        public Task Handle(TestDomainEvent notification, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }

    public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public ProductCreatedEvent? ReceivedEvent { get; private set; } = null;

        public Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }

    public class OrderSubmittedEventHandler : INotificationHandler<OrderSubmittedEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public OrderSubmittedEvent? ReceivedEvent { get; private set; } = null;

        public Task Handle(OrderSubmittedEvent notification, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }
}
