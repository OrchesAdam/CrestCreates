using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.CodeGenerator.Tests.Services
{
    /// <summary>
    /// 产品服务接口 - ServiceGenerator 测试示例
    /// </summary>
    public interface IProductService
    {
        Task<ProductDto> GetByIdAsync(Guid id);
        Task<List<ProductDto>> GetAllAsync();
        Task<ProductDto> CreateAsync(CreateProductDto dto);
        Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto dto);
        Task DeleteAsync(Guid id);
        Task<List<ProductDto>> GetByCategoryAsync(string category);
    }

    /// <summary>
    /// 产品服务实现 - ServiceGenerator 测试示例
    /// 演示如何使用 [Service] 特性自动生成：
    /// 1. 服务注册扩展方法
    /// 2. RESTful API 控制器
    /// 3. 服务扩展方法
    /// 4. 测试基类
    /// </summary>
    [CrestService(
        Lifetime = CrestServiceAttribute.ServiceLifetime.Scoped,
        GenerateController = true,
        Route = "api/products"
    )]
    public class ProductService : IProductService
    {
        public async Task<ProductDto> GetByIdAsync(Guid id)
        {
            // 业务逻辑实现
            await Task.CompletedTask;
            return new ProductDto { Id = id, Name = "Sample Product" };
        }

        public async Task<List<ProductDto>> GetAllAsync()
        {
            await Task.CompletedTask;
            return new List<ProductDto>();
        }

        public async Task<ProductDto> CreateAsync(CreateProductDto dto)
        {
            await Task.CompletedTask;
            return new ProductDto { Id = Guid.NewGuid(), Name = dto.Name };
        }

        public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto dto)
        {
            await Task.CompletedTask;
            return new ProductDto { Id = id, Name = dto.Name };
        }

        public async Task DeleteAsync(Guid id)
        {
            await Task.CompletedTask;
        }

        public async Task<List<ProductDto>> GetByCategoryAsync(string category)
        {
            await Task.CompletedTask;
            return new List<ProductDto>();
        }
    }

    #region DTOs

    /// <summary>
    /// 产品数据传输对象
    /// </summary>
    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    /// <summary>
    /// 创建产品请求对象
    /// </summary>
    public class CreateProductDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    /// <summary>
    /// 更新产品请求对象
    /// </summary>
    public class UpdateProductDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    #endregion
}
