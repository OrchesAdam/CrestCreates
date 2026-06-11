namespace CrestCreates.Organization.Abstractions;

public enum DataPermissionScopeKind
{
    None,
    Self,
    OwnOrganization,
    OwnOrganizationAndDescendants,
    All
}
