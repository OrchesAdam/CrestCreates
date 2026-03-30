using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using Ecommerce.Domain.Entities;

namespace Ecommerce.Domain.Repositories
{
    public interface IProductRepository : IRepository<Product, int>
    {
        Task<Product> GetByNameAsync(string name);
        Task<List<Product>> GetActiveProductsAsync(int page = 1, int pageSize = 10);
        Task<List<Product>> GetOutOfStockProductsAsync();
        Task<decimal> GetAveragePriceAsync();
    }
}