using CrestCreates.Domain.DomainEvents;
using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.DomainEvents
{
    public class ProductPriceChangedEvent : DomainEvent
    {
        public Product Product { get; }

        public ProductPriceChangedEvent(Product product)
        {
            Product = product;
        }
    }

    public class ProductStockReducedEvent : DomainEvent
    {
        public Product Product { get; }
        public int Quantity { get; }

        public ProductStockReducedEvent(Product product, int quantity)
        {
            Product = product;
            Quantity = quantity;
        }
    }

    public class ProductStockIncreasedEvent : DomainEvent
    {
        public Product Product { get; }
        public int Quantity { get; }

        public ProductStockIncreasedEvent(Product product, int quantity)
        {
            Product = product;
            Quantity = quantity;
        }
    }
}