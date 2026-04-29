using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public interface ITenantDbContextFactory
    {
        TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext;
    }
}
