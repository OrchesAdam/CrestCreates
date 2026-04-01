namespace CrestCreates.DataFilter.Entities;

public interface IMultiTenant
{
    string? TenantId { get; set; }
}