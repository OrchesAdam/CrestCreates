using Microsoft.EntityFrameworkCore;

namespace CrestCreates.EventBus.DeadLetter.EFCore;

public sealed class DeadLetterDbContext : DbContext
{
    public DbSet<DeadLetterEntity> DeadLetters => Set<DeadLetterEntity>();

    public DeadLetterDbContext(DbContextOptions<DeadLetterDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeadLetterEntity>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.HasIndex(e => new { e.EventName, e.EventVersion });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FailedAt);
        });
    }
}
