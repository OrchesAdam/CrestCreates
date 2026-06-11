namespace CrestCreates.Organization.Abstractions;

public sealed class OrganizationContext
{
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string? PrimaryOrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoleIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PositionIds { get; init; } = Array.Empty<string>();
}
