namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();
}
