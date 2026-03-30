using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Examples.DTOs;

namespace CrestCreates.Application.Contracts.Examples.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> GetByIdAsync(Guid id);
        Task<List<ProductDto>> GetAllAsync();
        Task<ProductDto> CreateAsync(CreateProductDto input);
        Task<ProductDto> UpdateAsync(UpdateProductDto input);
        Task DeleteAsync(Guid id);
        Task<ProductDto> UpdatePriceAsync(UpdateProductPriceDto input);
        Task<ProductDto> AddStockAsync(UpdateProductStockDto input);
        Task<ProductDto> RemoveStockAsync(UpdateProductStockDto input);
    }
}
