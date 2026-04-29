using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class CrestCreatesDbContextFactory : IDesignTimeDbContextFactory<CrestCreatesDbContext>
    {
        public CrestCreatesDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<CrestCreatesDbContext>();

            // Design-time only. Keep the runtime provider choice in the application-level DbContext options path.
            optionsBuilder.UseSqlite("Data Source=:memory:");

            return new CrestCreatesDbContext(optionsBuilder.Options);
        }
    }
}
