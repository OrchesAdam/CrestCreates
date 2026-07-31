using System.Text.Json.Serialization;

namespace CrestCreates.Runtime.Persistence.Abstractions.Keys;

public readonly record struct RuntimeTenantScope
{
    [JsonConstructor]
    public RuntimeTenantScope(string? tenantId)
    {
        if (tenantId is not null && string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID must be null for host scope or a nonblank exact tenant ID.",
                nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public string? TenantId { get; }

    public bool IsHost => TenantId is null;

    public void EnsureValid()
    {
        if (TenantId is not null && string.IsNullOrWhiteSpace(TenantId))
        {
            throw new ArgumentException(
                "Tenant ID must be null for host scope or a nonblank exact tenant ID.",
                nameof(TenantId));
        }
    }
}
