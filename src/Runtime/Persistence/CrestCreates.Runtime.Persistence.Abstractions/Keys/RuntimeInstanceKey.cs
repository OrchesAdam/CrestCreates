namespace CrestCreates.Runtime.Persistence.Abstractions.Keys;

public readonly record struct RuntimeInstanceKey
{
    public RuntimeInstanceKey(string? tenantId, string instanceId)
    {
        if (tenantId is not null && string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID must be null for host scope or a nonblank exact tenant ID.",
                nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        TenantId = tenantId;
        InstanceId = instanceId;
    }

    public string? TenantId { get; }

    public string InstanceId { get; } = string.Empty;

    public void EnsureValid()
    {
        if (TenantId is not null && string.IsNullOrWhiteSpace(TenantId))
        {
            throw new ArgumentException(
                "Tenant ID must be null for host scope or a nonblank exact tenant ID.",
                nameof(TenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(InstanceId);
    }
}
