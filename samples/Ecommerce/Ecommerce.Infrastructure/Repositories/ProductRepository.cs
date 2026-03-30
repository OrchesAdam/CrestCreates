using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Repositories;
using Ecommerce.Infrastructure.DbContexts;

namespace Ecommerce.Infrastructure.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly EcommerceDbContext _dbContext;

        public ProductRepository(EcommerceDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Product> AddAsync(Product entity)
        {
            await _dbContext.Products.AddAsync(entity);
            return entity;
        }

        public async Task DeleteAsync(Product entity)
        {
            _dbContext.Products.Remove(entity);
            await Task.CompletedTask;
        }

        public async Task<Product> GetByIdAsync(int id)
        {
            return await _dbContext.Products.FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task<Product> GetByNameAsync(string name)
        {
            return await _dbContext.Products.FirstOrDefaultAsync(e => e.Name == name);
        }

        public async Task<List<Product>> GetActiveProductsAsync(int page = 1, int pageSize = 10)
        {
            return await _dbContext.Products
                .Where(e => e.IsActive)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<List<Product>> GetOutOfStockProductsAsync()
        {
            return await _dbContext.Products
                .Where(e => e.Stock <= 0)
                .ToListAsync();
        }

        public async Task<decimal> GetAveragePriceAsync()
        {
            return await _dbContext.Products
                .Where(e => e.IsActive)
                .AverageAsync(e => e.Price);
        }

        public async Task<List<Product>> GetAllAsync()
        {
            return await _dbContext.Products.ToListAsync();
        }

        public async Task<Product> UpdateAsync(Product entity)
        {
            _dbContext.Products.Update(entity);
            return entity;
        }

        public async Task<List<Product>> FindAsync(System.Linq.Expressions.Expression<System.Func<Product, bool>> predicate)
        {
            return await _dbContext.Products.Where(predicate).ToListAsync();
        }
    }
}