using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Application.Settings;

public sealed class SettingManagementPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull("SettingManagement")
                    ?? context.AddGroup("SettingManagement", "Setting management");
        group.AddPermission(SettingManagementPermissions.Read, "Read host, tenant and user setting values");
        group.AddPermission(SettingManagementPermissions.Manage, "Update and delete host, tenant and user setting values");
    }
}
