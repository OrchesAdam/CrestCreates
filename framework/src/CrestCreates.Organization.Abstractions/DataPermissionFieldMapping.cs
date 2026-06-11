namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFieldMapping
{
    public string? UserIdField { get; init; }
    public string? OrganizationUnitIdField { get; init; }
    public string? TenantIdField { get; init; }

    public bool HasUserIdField => !string.IsNullOrEmpty(UserIdField);
    public bool HasOrganizationUnitIdField => !string.IsNullOrEmpty(OrganizationUnitIdField);
    public bool HasTenantIdField => !string.IsNullOrEmpty(TenantIdField);
}
