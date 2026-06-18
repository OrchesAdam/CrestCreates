using Microsoft.EntityFrameworkCore;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Data.EFCore.MultiTenancy
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
        private readonly Func<string, DbContext> _tenantDbContextFactory;

        public TenantDbContextFactory(
            ITenantConnectionStringResolver connectionStringResolver,
            ITenantDbContextFactory dbContextFactory,
            Func<string, DbContext> tenantDbContextFactory)
        {
            _connectionStringResolver = connectionStringResolver;
            _dbContextFactory = dbContextFactory;
            _tenantDbContextFactory = tenantDbContextFactory;
        }

        public TDbContext CreateDbContext()
        {
            var connectionString = _connectionStringResolver.Resolve();
            return (TDbContext)_tenantDbContextFactory(connectionString);
        }
    }
}
