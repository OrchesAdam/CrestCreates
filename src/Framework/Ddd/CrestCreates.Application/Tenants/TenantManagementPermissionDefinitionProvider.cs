using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.Tenants;

public sealed class TenantManagementPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("TenantManagement")
                    ?? context.AddGroup("TenantManagement", "Tenant management");
        group.AddPermission(TenantManagementPermissions.View, "Read tenants and their initialization status");
        group.AddPermission(TenantManagementPermissions.Update, "Create, update, activate and re-initialize tenants");
    }
}