namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class Statistics
{
    public int UserCount { get; set; }
    public int RoleCount { get; set; }
    public int PermissionGrantCount { get; set; }
    public int DomainMappingCount { get; set; }
}
