using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Domain.Permission;

public class PermissionGrant : Entity<Guid>
{
    public string PermissionName { get; set; } = string.Empty;
    public PermissionGrantProviderType ProviderType { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public PermissionGrantScope Scope { get; set; } = PermissionGrantScope.Global;
    public string? TenantId { get; set; }

    public PermissionGrant()
    {
    }

    public PermissionGrant(
        Guid id,
        string permissionName,
        PermissionGrantProviderType providerType,
        string providerKey,
        PermissionGrantScope scope = PermissionGrantScope.Global,
        string? tenantId = null)
    {
        Id = id;
        PermissionName = permissionName;
        ProviderType = providerType;
        ProviderKey = providerKey;
        Scope = scope;
        TenantId = scope == PermissionGrantScope.Global ? null : tenantId;
    }
}
