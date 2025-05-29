using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.Data.Context;

namespace CrestCreates.Repositories.EFCore
{    /// <summary>
    /// Entity Framework Core 数据库上下文实现
    /// </summary>
    public class EFCoreDbContext : DbContext, IDbContext
    {
        private IDbContextTransaction? _currentTransaction;

        public EFCoreDbContext(DbContextOptions<EFCoreDbContext> options) : base(options)
        {
        }public new IDbSet<TEntity> Set<TEntity>() where TEntity : class
        {
            return new EFCoreDbSet<TEntity>(base.Set<TEntity>());
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            return base.SaveChanges();
        }

        public async Task<Data.Context.IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
            return new EFCoreTransaction(_currentTransaction);
        }

        public Data.Context.IDbTransaction? CurrentTransaction => 
            _currentTransaction != null ? new EFCoreTransaction(_currentTransaction) : null;

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                return false;
            }
        }        public Task<T[]> ExecuteQueryAsync<T>(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            // EF Core doesn't directly support raw SQL queries that return arbitrary types
            // This would need to be implemented based on specific requirements
            throw new NotImplementedException("Raw SQL queries need to be implemented based on specific entity types");
        }

        public async Task<int> ExecuteCommandAsync(string sql, object[]? parameters = null, CancellationToken cancellationToken = default)
        {
            return await Database.ExecuteSqlRawAsync(sql, parameters ?? Array.Empty<object>(), cancellationToken);
        }        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // 这里可以添加全局配置
            // 例如：配置实体约定、全局查询过滤器等
        }

        public override async ValueTask DisposeAsync()
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
            await base.DisposeAsync();
        }
    }
}
