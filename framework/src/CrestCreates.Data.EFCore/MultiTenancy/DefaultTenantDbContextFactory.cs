using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    public class DefaultTenantDbContextFactory : ITenantDbContextFactory
    {
        public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext
        {
            return (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;
        }
    }
}
