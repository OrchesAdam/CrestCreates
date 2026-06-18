namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantContext
{
    string? CurrentTenantId { get; }
}