using System;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantStatusDetails
{
    public string LifecycleState { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ArchivedTime { get; set; }
    public DateTime? CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}
