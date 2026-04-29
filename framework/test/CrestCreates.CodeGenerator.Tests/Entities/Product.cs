using System;
using System.ComponentModel.DataAnnotations;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Entities
{
    [Entity(
        GenerateRepository = true,
        GenerateAuditing = true,
        OrmProvider = "EfCore",
        TableName = "Products"
    )]
    public class Product : FullyAuditedAggregateRoot<Guid>
    {
        [Required]
        [StringLength(100)]
        public string Name { get; private set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; private set; }
        
        [Required]
        public decimal Price { get; private set; }
        
        public int StockQuantity { get; private set; }
        
        [Required]
        [StringLength(50)]
        public string Category { get; private set; } = string.Empty;
        
        public bool IsActive { get; private set; } = true;
        
        public DateTime? LaunchDate { get; private set; }

        public Product()
        {
            // ORM构造函数
        }

        public Product(string name, decimal price, string category)
            : base()
        {
            SetName(name);
            SetPrice(price);
            SetCategory(category);
            IsActive = true;
        }

        public void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("产品名称不能为空", nameof(name));
            
            if (name.Length > 100)
                throw new ArgumentException("产品名称长度不能超过100个字符", nameof(name));

            Name = name.Trim();
        }

        public void SetDescription(string? description)
        {
            if (description?.Length > 500)
                throw new ArgumentException("产品描述长度不能超过500个字符", nameof(description));

            Description = description?.Trim();
        }

        public void SetPrice(decimal price)
        {
            if (price < 0)
                throw new ArgumentException("产品价格不能为负数", nameof(price));

            Price = price;
        }

        public void SetStockQuantity(int quantity)
        {
            if (quantity < 0)
                throw new ArgumentException("库存数量不能为负数", nameof(quantity));

            StockQuantity = quantity;
        }

        public void SetCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("产品分类不能为空", nameof(category));

            Category = category.Trim();
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        public void Launch(DateTime launchDate)
        {
            if (launchDate < DateTime.Today)
                throw new ArgumentException("发布日期不能是过去时间", nameof(launchDate));

            LaunchDate = launchDate;
        }

        public bool IsInStock()
        {
            return StockQuantity > 0;
        }

        public void ReduceStock(int quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("减少数量必须大于0", nameof(quantity));

            if (StockQuantity < quantity)
                throw new InvalidOperationException("库存不足");

            StockQuantity -= quantity;
        }

        public void AddStock(int quantity)
        {
            if (quantity <= 0)
                throw new ArgumentException("增加数量必须大于0", nameof(quantity));

            StockQuantity += quantity;
        }
    }
}