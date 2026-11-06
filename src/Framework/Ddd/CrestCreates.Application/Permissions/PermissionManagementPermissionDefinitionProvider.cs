using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.Permissions;

public sealed class PermissionManagementPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("PermissionManagement")
                    ?? context.AddGroup("PermissionManagement", "Permission management");
        group.AddPermission(PermissionManagementPermissions.Read, "Read permission grants and effective permissions");
        group.AddPermission(PermissionManagementPermissions.Manage, "Grant and revoke permissions");
    }
}
