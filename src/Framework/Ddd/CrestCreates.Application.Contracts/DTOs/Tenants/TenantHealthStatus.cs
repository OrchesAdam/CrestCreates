using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantHealthStatus
{
    public bool IsHealthy { get; set; }
    public bool IsActive { get; set; }
    public bool IsArchived { get; set; }
    public string Level { get; set; } = "Healthy";
    public List<string> Issues { get; set; } = new();
}
