using System;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantInitializationStep
{
    public string Name { get; set; } = string.Empty;
    public TenantInitializationStepStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public enum TenantInitializationStepStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped
}
