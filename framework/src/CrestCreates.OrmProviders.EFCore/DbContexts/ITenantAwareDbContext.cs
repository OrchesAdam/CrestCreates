namespace CrestCreates.OrmProviders.EFCore.DbContexts;

public interface ITenantAwareDbContext
{
    string? CurrentTenantId { get; }
}
