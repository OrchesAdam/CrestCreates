using System;
using CrestCreates.Domain.DomainEvents;

namespace CrestCreates.Domain.Examples
{
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
    
    public class ProductPriceChangedEvent : DomainEvent
    {
        public Guid ProductId { get; }
        public Money NewPrice { get; }
        
        public ProductPriceChangedEvent(Guid productId, Money newPrice)
        {
            ProductId = productId;
            NewPrice = newPrice;
        }
    }
    
    public class ProductOutOfStockEvent : DomainEvent
    {
        public Guid ProductId { get; }
        
        public ProductOutOfStockEvent(Guid productId)
        {
            ProductId = productId;
        }
    }
}
