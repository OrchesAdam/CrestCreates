using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Organization.Abstractions;

public sealed class UserOrganizationRoleAssignment : ISnapshotable<UserOrganizationRoleAssignment>
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string RoleId { get; init; } = default!;
    public string? OrganizationUnitId { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public UserOrganizationRoleAssignment Snapshot() => new()
    {
        Id = Id,
        TenantId = TenantId,
        UserId = UserId,
        RoleId = RoleId,
        OrganizationUnitId = OrganizationUnitId,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
}
