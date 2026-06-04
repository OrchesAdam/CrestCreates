namespace CrestCreates.Data.EFCore.DbContexts;

public interface ITenantAwareDbContext
{
    string? CurrentTenantId { get; }
}
