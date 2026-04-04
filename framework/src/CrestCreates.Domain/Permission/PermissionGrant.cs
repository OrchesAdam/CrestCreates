using System;
using CrestCreates.Domain.Entities;

namespace CrestCreates.Domain.Permission;

public class PermissionGrant : Entity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? TenantId { get; set; }

    public PermissionGrant()
    {
    }

    public PermissionGrant(Guid id, string name, string providerName, string providerKey, string? tenantId = null)
    {
        Id = id;
        Name = name;
        ProviderName = providerName;
        ProviderKey = providerKey;
        TenantId = tenantId;
    }

    public static class ProviderNames
    {
        public const string Role = "R";
        public const string User = "U";
    }
}
