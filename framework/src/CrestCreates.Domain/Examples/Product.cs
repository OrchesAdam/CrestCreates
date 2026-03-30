using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.ValueObjects;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.Examples;

namespace CrestCreates.Domain.Examples
{
    [Entity(
        GenerateRepository = true,
        GenerateAuditing = true,
        OrmProvider = "EfCore",
        TableName = "Products"
    )]
    public class Product : FullyAuditedAggregateRoot<Guid>
    {        public string? Name { get; private set; }
        public string? Description { get; private set; }
        public Money? Price { get; private set; }
        public ProductType Type { get; private set; }
        public int StockCount { get; private set; }
        
        private Product()
        {
            // 为EF Core保留的私有构造函数
        }
        
        public Product(Guid id, string name, string description, Money price, ProductType type)
            : base()
        {
            Id = id;
            SetName(name);
            SetDescription(description);
            Price = price;
            Type = type;
            StockCount = 0;
            
            // 添加领域事件
            AddDomainEvent(new ProductCreatedEvent(id, name));
        }
        
        public void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("产品名称不能为空", nameof(name));
                
            if (name.Length > 100)
                throw new ArgumentException("产品名称不能超过100个字符", nameof(name));
                
            Name = name;
        }
        
        public void SetDescription(string description)
        {
            Description = description ?? string.Empty;
        }
        
        public void UpdatePrice(Money newPrice)
        {
            if (newPrice.Amount < 0)
                throw new ArgumentException("产品价格不能为负", nameof(newPrice));
                
            Price = newPrice;
            AddDomainEvent(new ProductPriceChangedEvent(Id, newPrice));
        }
        
        public void AddStock(int quantity)
        {
            if (quantity < 0)
                throw new ArgumentException("入库数量不能为负", nameof(quantity));
                
            StockCount += quantity;
        }
        
        public void RemoveStock(int quantity)
        {
            if (quantity < 0)
                throw new ArgumentException("出库数量不能为负", nameof(quantity));
                
            if (StockCount < quantity)
                throw new InvalidOperationException("库存不足");
                
            StockCount -= quantity;
            
            if (StockCount == 0)
            {
                AddDomainEvent(new ProductOutOfStockEvent(Id));
            }
        }
    }
}
