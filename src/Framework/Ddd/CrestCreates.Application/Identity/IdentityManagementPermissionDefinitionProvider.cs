using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.Identity;

public sealed class IdentityManagementPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("IdentityManagement")
                    ?? context.AddGroup("IdentityManagement", "Identity management");
        group.AddPermission(IdentityManagementPermissions.UsersRead, "Read users and their roles");
        group.AddPermission(IdentityManagementPermissions.UsersManage, "Create, update, deactivate, reset passwords and assign roles");
        group.AddPermission(
            IdentityManagementPermissions.UsersManageSuperAdmin,
            "Promote or demote super administrators");
    }
}
