using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Tests.Events;

namespace CrestCreates.EventBus.Tests.Handlers
{
    public class TestDomainEventHandler : ILocalEventHandler<TestLocalEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public TestLocalEvent? ReceivedEvent { get; private set; } = null;

        public Task HandleAsync(TestLocalEvent notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }

    public class ProductCreatedEventHandler : ILocalEventHandler<ProductCreatedEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public ProductCreatedEvent? ReceivedEvent { get; private set; } = null;

        public Task HandleAsync(ProductCreatedEvent notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }

    public class OrderSubmittedEventHandler : ILocalEventHandler<OrderSubmittedEvent>
    {
        public bool WasCalled { get; private set; } = false;
        public OrderSubmittedEvent? ReceivedEvent { get; private set; } = null;

        public Task HandleAsync(OrderSubmittedEvent notification, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            ReceivedEvent = notification;
            return Task.CompletedTask;
        }
    }
}
