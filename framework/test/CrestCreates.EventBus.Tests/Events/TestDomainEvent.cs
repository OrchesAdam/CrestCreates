using System;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.EventBus.Tests.Events
{
    public class TestDomainEvent : DomainEvent
    {
        public Guid EntityId { get; }

        public TestDomainEvent(Guid entityId)
        {
            EntityId = entityId;
        }
    }

    public class ProductCreatedEvent : DomainEvent
    {
        public Guid ProductId { get; }
        public string ProductName { get; }

        public ProductCreatedEvent(Guid productId, string productName)
        {
            ProductId = productId;
            ProductName = productName;
        }
    }

    public class OrderSubmittedEvent : DomainEvent
    {
        public Guid OrderId { get; }
        public decimal TotalAmount { get; }

        public OrderSubmittedEvent(Guid orderId, decimal totalAmount)
        {
            OrderId = orderId;
            TotalAmount = totalAmount;
        }
    }
}
