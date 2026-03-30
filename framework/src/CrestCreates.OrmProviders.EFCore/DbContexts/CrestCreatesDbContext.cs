using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class CrestCreatesDbContext : DbContext, IEntityFrameworkCoreDbContext
    {
        public CrestCreatesDbContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
            
        }

        // DbSet properties for your entities go here
        // Example:
        // public DbSet<YourEntity> YourEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Configure your entity mappings here
        }

        // IEntityFrameworkCoreDbContext implementation
        public OrmProvider Provider => OrmProvider.EfCore;

        public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
        {
            return new EfCoreDataBaseSet<TEntity>(base.Set<TEntity>());
        }

        public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return base.SaveChangesAsync(cancellationToken);
        }

        public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await Database.BeginTransactionAsync(cancellationToken);
            // 传入 this 引用，让 Transaction 可以访问 DbContext 的属性
            return new EfCoreDataBaseTransaction(transaction, this);
        }

        public IDataBaseTransaction CurrentTransaction => 
            Database.CurrentTransaction != null 
                ? new EfCoreDataBaseTransaction(Database.CurrentTransaction, this) 
                : null;

        public string ConnectionString => Database.GetConnectionString();

        public object GetNativeContext() => this;

        public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
        {
            return new EfCoreQueryableBuilder<TEntity>(base.Set<TEntity>());
        }

        public Task<int> ExecuteSqlRawAsync(string sql, IEnumerable<object> parameters = null, CancellationToken cancellationToken = default)
        {
            return Database.ExecuteSqlRawAsync(sql, parameters ?? new object[0], cancellationToken);
        }

        public new void Dispose()
        {
            base.Dispose();
        }
    }
}