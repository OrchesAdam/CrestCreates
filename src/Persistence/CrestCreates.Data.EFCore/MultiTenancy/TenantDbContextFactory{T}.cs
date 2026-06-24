using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
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
