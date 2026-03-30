using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.DomainEvents;
using Ecommerce.Domain.DomainEvents;

namespace Ecommerce.Domain.Entities
{
    public class Product : AggregateRoot<int>
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }

        public void ChangePrice(decimal newPrice)
        {
            Price = newPrice;
            AddDomainEvent(new ProductPriceChangedEvent(this));
        }

        public void ReduceStock(int quantity)
        {
            if (Stock < quantity)
            {
                throw new InvalidOperationException("Insufficient stock");
            }

            Stock -= quantity;
            AddDomainEvent(new ProductStockReducedEvent(this, quantity));
        }

        public void IncreaseStock(int quantity)
        {
            Stock += quantity;
            AddDomainEvent(new ProductStockIncreasedEvent(this, quantity));
        }
    }
}