using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecommerce.Application.Contracts.DTOs;

namespace Ecommerce.Application.Contracts.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default);
        Task<ProductDto> UpdateAsync(int id, UpdateProductDto dto, CancellationToken cancellationToken = default);
        Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<ProductDto> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<ProductListDto> GetActiveProductsAsync(int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
        Task<List<ProductDto>> GetOutOfStockProductsAsync(CancellationToken cancellationToken = default);
        Task<decimal> GetAveragePriceAsync(CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
        Task ReduceStockAsync(int id, int quantity, CancellationToken cancellationToken = default);
        Task IncreaseStockAsync(int id, int quantity, CancellationToken cancellationToken = default);
    }
}