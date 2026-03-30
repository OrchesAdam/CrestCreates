using System;
using CrestCreates.Domain.Shared.Examples;

namespace CrestCreates.Application.Contracts.Examples.DTOs
{
    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; }
        public ProductType Type { get; set; }
        public int StockCount { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime? LastModificationTime { get; set; }
    }
    
    public class CreateProductDto
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "CNY";
        public ProductType Type { get; set; }
    }
    
    public class UpdateProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
    
    public class UpdateProductPriceDto
    {
        public Guid Id { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "CNY";
    }
    
    public class UpdateProductStockDto
    {
        public Guid Id { get; set; }
        public int Quantity { get; set; }
    }
}
