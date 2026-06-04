using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.MultiTenancy
{
    public interface ITenantDbContextFactory
    {
        TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext;
    }
}
