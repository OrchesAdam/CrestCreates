namespace CrestCreates.Application.Contracts.Permissions;

/// <summary>
/// Wire-level permission constants for the platform management HTTP surface (G-09).
/// Lives in Contracts because <c>[AppServiceHttpAction]</c> attributes are declared on the
/// service interfaces here, and Contracts cannot reference Application. Values are kept
/// byte-identical to the Application-layer admission constants
/// (IdentityManagementPermissions / PermissionManagementPermissions / SettingManagementPermissions /
/// FeatureManagementPermissions); the two sets must stay aligned.
/// </summary>
public static class PlatformHttpPermissions
{
    public static class Users
    {
        public const string Read = "IdentityManagement.Users.Read";
        public const string Manage = "IdentityManagement.Users.Manage";
        public const string ManageSuperAdmin = "IdentityManagement.Users.ManageSuperAdmin";
    }

    public static class PermissionGrants
    {
        public const string Read = "PermissionManagement.Read";
        public const string Manage = "PermissionManagement.Manage";
    }

    public static class Settings
    {
        public const string Read = "SettingManagement.Read";
        public const string Manage = "SettingManagement.Manage";
    }

    public static class Features
    {
        public const string Read = "FeatureManagement.Read";
        public const string ManageGlobal = "FeatureManagement.ManageGlobal";
        public const string ManageTenant = "FeatureManagement.ManageTenant";
    }

    public static class Tenants
    {
        public const string View = "TenantManagement.View";
        public const string Update = "TenantManagement.Update";
    }

    public static class AuditLog
    {
        public const string Read = "AuditLogging.Read";
    }
}