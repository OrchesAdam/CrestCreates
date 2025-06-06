using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Infrastructure.EntityFrameworkCore.DbContexts
{
    public class CrestCreatesDbContext : DbContext
    {
        public CrestCreatesDbContext(DbContextOptions<CrestCreatesDbContext> options)
            : base(options)
        {
        }

        // DbSet properties for your entities go here
        // Example:
        // public DbSet<YourEntity> YourEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Configure your entity mappings here
        }
    }
}