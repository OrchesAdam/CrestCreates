using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Abstractions;
using CrestCreates.Domain.Examples;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class CrestCreatesDbContext : DbContext, IEntityFrameworkCoreDbContext
    {
        public CrestCreatesDbContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
            
        }

        // DbSet properties for your entities
        public DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure Product entity
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");
                
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(1000);
                
                // Map Money value object as owned entity
                entity.OwnsOne(e => e.Price, price =>
                {
                    price.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                    price.Property(p => p.Currency).HasMaxLength(3);
                });
                
                // Map ProductType enum as int
                entity.Property(e => e.Type).HasConversion<int>();
                
                entity.Property(e => e.StockCount).IsRequired();
                
                // Audit fields
                entity.Property(e => e.CreationTime).IsRequired();
                entity.Property(e => e.CreatorId);
                entity.Property(e => e.LastModificationTime);
                entity.Property(e => e.LastModifierId);
                
                // Soft delete
                entity.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.DeletionTime);
                entity.Property(e => e.DeleterId);
                
                // Global filter for soft delete
                entity.HasQueryFilter(e => !e.IsDeleted);
            });
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