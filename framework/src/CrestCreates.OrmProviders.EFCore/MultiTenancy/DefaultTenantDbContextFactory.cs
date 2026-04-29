using System;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.MultiTenancy
{
    public class DefaultTenantDbContextFactory : ITenantDbContextFactory
    {
        public TDbContext Create<TDbContext>(DbContextOptions<TDbContext> options) where TDbContext : DbContext
        {
            return (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;
        }
    }
}
