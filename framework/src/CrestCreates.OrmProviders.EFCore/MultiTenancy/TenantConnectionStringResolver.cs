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

            return _currentTenant.Tenant.ConnectionString
                ?? throw new InvalidOperationException($"Tenant '{_currentTenant.Tenant.Name}' has no connection string configured.");
        }
    }

    public interface ITenantConnectionStringResolver
    {
        string Resolve();
    }

    public class TenantDbContextFactory<TDbContext> : IDbContextFactory<TDbContext>
        where TDbContext : DbContext
    {
        private readonly ITenantConnectionStringResolver _connectionStringResolver;
        private readonly ITenantDbContextFactory _dbContextFactory;

        public TenantDbContextFactory(
            ITenantConnectionStringResolver connectionStringResolver,
            ITenantDbContextFactory dbContextFactory)
        {
            _connectionStringResolver = connectionStringResolver;
            _dbContextFactory = dbContextFactory;
        }

        public TDbContext CreateDbContext()
        {
            var connectionString = _connectionStringResolver.Resolve();

            var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return _dbContextFactory.Create<TDbContext>(optionsBuilder.Options);
        }
    }
}
