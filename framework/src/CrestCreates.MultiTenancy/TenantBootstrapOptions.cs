namespace CrestCreates.MultiTenancy;

public class TenantBootstrapOptions
{
    public bool EnableAutoBootstrap { get; set; } = true;
    public string DefaultAdminUserName { get; set; } = "admin";
    public string DefaultAdminEmail { get; set; } = "admin@{0}.local";
    public string DefaultAdminPassword { get; set; } = "Admin123!";
    public string DefaultRoleName { get; set; } = "Default";
    public string DefaultRoleDisplayName { get; set; } = "默认角色";
    public bool BootstrapAdminRole { get; set; } = true;
    public bool BootstrapBasicPermissions { get; set; } = true;
    public string[] BasicPermissions { get; set; } = new[]
    {
        "TenantManagement.View",
        "TenantManagement.Update",
        "Users.View",
        "Users.Create",
        "Users.Update",
        "Users.Delete",
        "Roles.View",
        "Roles.Create",
        "Roles.Update",
        "Roles.Delete"
    };
}
