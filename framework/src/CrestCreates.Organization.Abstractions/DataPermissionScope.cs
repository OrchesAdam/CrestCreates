namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? Resource { get; init; }
    public string? Action { get; init; }
    public string? Permission { get; init; }
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();

    public bool IsEmpty => Kind == DataPermissionScopeKind.None;
    public bool IsUnrestricted => Kind == DataPermissionScopeKind.All;
}
