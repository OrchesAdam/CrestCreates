using CrestCreates.Application.Contracts.Permissions;

namespace CrestCreates.Application.Tenants;

public static class TenantManagementPermissions
{
    public const string View = PlatformHttpPermissions.Tenants.View;
    public const string Update = PlatformHttpPermissions.Tenants.Update;
}