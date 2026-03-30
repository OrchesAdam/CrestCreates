using System;
using Microsoft.EntityFrameworkCore;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class TenantConnectionStringResolver : ITenantConnectionStringResolver
    {
        private readonly ICurrentTenant _currentTenant;

        public TenantConnectionStringResolver(ICurrentTenant currentTenant)
        {
            _currentTenant = currentTenant;
        }

        public string Resolve()
        {
            if (_currentTenant?.Tenant == null)
            {
                throw new InvalidOperationException("No tenant is available in the current context.");
            }

            return _currentTenant.Tenant.ConnectionString;
        }
    }

    public interface ITenantConnectionStringResolver
    {
        string Resolve();
    }
}

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class TenantDbContextFactory<TDbContext> : IDbContextFactory<TDbContext> 
        where TDbContext : DbContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITenantConnectionStringResolver _connectionStringResolver;

        public TenantDbContextFactory(
            IServiceProvider serviceProvider, 
            ITenantConnectionStringResolver connectionStringResolver)
        {
            _serviceProvider = serviceProvider;
            _connectionStringResolver = connectionStringResolver;
        }

        public TDbContext CreateDbContext()
        {
            var connectionString = _connectionStringResolver.Resolve();
            
            var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
            optionsBuilder.UseSqlServer(connectionString); // 或者使用其他数据库提供程序
            
            return (TDbContext)Activator.CreateInstance(typeof(TDbContext), optionsBuilder.Options);
        }
    }
}
