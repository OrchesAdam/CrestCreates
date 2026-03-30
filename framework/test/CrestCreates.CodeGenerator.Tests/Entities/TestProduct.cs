using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities
{
    [Entity(
        GenerateRepository = true,
        GenerateAuditing = true,
        OrmProvider = "EfCore",
        TableName = "TestProducts"
    )]
    public class TestProduct : FullyAuditedAggregateRoot<Guid>
    {
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public decimal Price { get; private set; }
        public int Stock { get; private set; }
        public bool IsActive { get; private set; } = true;        public TestProduct()
        {
            // EF Core构造函数
        }

        public TestProduct(Guid id, string name, string description, decimal price)
            : base()
        {
            Id = id;
            SetName(name);
            SetDescription(description);
            SetPrice(price);
            Stock = 0;
        }

        public void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("产品名称不能为空", nameof(name));
            
            Name = name.Trim();
        }

        public void SetDescription(string description)
        {
            Description = description?.Trim() ?? string.Empty;
        }

        public void SetPrice(decimal price)
        {
            if (price < 0)
                throw new ArgumentException("价格不能为负数", nameof(price));
            
            Price = price;
        }

        public void AddStock(int quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("数量必须大于0", nameof(quantity));
            
            Stock += quantity;
        }

        public void RemoveStock(int quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("数量必须大于0", nameof(quantity));
            
            if (Stock < quantity)
                throw new InvalidOperationException("库存不足");
            
            Stock -= quantity;
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }
    }
}
