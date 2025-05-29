using System;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Adapters;
using CrestCreates.Data.Context;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Repositories.EFCore
{
    /// <summary>
    /// Entity Framework Core ORM 适配器
    /// </summary>
    public class EFCoreOrmAdapter : OrmAdapterBase
    {
        public override string Name => "Entity Framework Core";
        public override OrmType OrmType => OrmType.EntityFrameworkCore;

        public override bool SupportsDatabase(DatabaseType databaseType)
        {
            return databaseType switch
            {
                DatabaseType.SqlServer => true,
                DatabaseType.MySQL => true,
                DatabaseType.PostgreSQL => true,
                DatabaseType.SQLite => true,
                DatabaseType.InMemory => true,
                _ => false
            };
        }

        public override IDbContext CreateDbContext(string connectionString, DatabaseOptions? options = null)
        {
            var optionsBuilder = new DbContextOptionsBuilder<EFCoreDbContext>();
            ConfigureDatabase(optionsBuilder, connectionString, options?.DatabaseType ?? DatabaseType.SqlServer);
            
            return new EFCoreDbContext(optionsBuilder.Options);
        }

        public override IRepository<TEntity> CreateRepository<TEntity>(IDbContext dbContext)
        {
            if (dbContext is not EFCoreDbContext efCoreContext)
            {
                throw new ArgumentException("DbContext must be of type EFCoreDbContext", nameof(dbContext));
            }

            return new EFCoreRepository<TEntity>(efCoreContext);
        }

        public override IUnitOfWork CreateUnitOfWork(IDbContext dbContext)
        {
            if (dbContext is not EFCoreDbContext efCoreContext)
            {
                throw new ArgumentException("DbContext must be of type EFCoreDbContext", nameof(dbContext));
            }

            return new EFCoreUnitOfWork(efCoreContext);
        }

        private static void ConfigureDatabase(DbContextOptionsBuilder optionsBuilder, string connectionString, DatabaseType databaseType)
        {
            switch (databaseType)
            {
                case DatabaseType.SqlServer:
                    optionsBuilder.UseSqlServer(connectionString);
                    break;
                case DatabaseType.MySQL:
                    // 需要 MySQL 包时可以添加
                    throw new NotSupportedException("MySQL support requires additional package reference");
                case DatabaseType.PostgreSQL:
                    // 需要 PostgreSQL 包时可以添加
                    throw new NotSupportedException("PostgreSQL support requires additional package reference");
                case DatabaseType.SQLite:
                    optionsBuilder.UseSqlite(connectionString);
                    break;
                case DatabaseType.InMemory:
                    optionsBuilder.UseInMemoryDatabase(connectionString);
                    break;
                default:
                    throw new NotSupportedException($"Database type '{databaseType}' is not supported by EF Core adapter");
            }
        }
    }
}
