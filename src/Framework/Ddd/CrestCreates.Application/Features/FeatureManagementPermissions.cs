using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Application.Features;

public static class FeatureManagementPermissions
{
    public const string ReadValue = "FeatureManagement.Read";
    public static PermissionName Read { get; } = new(ReadValue);

    public const string ManageGlobalValue = "FeatureManagement.ManageGlobal";
    public static PermissionName ManageGlobal { get; } = new(ManageGlobalValue);

    public const string ManageTenantValue = "FeatureManagement.ManageTenant";
    public static PermissionName ManageTenant { get; } = new(ManageTenantValue);
}
