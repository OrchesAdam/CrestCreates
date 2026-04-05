namespace CrestCreates.Aop.Abstractions.Options;

public class DataPermissionOptions
{
    public bool EnableDataPermission { get; set; } = true;
    public bool ApplyTenantFilter { get; set; } = true;
    public bool ApplyOrganizationFilter { get; set; } = true;
    public Dictionary<string, DataPermissionProfile> Profiles { get; set; } = new();
}

public class DataPermissionProfile
{
    public bool ApplyTenantFilter { get; set; } = true;
    public bool ApplyOrganizationFilter { get; set; } = true;
}
