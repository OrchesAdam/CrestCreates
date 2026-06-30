using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Organization.Abstractions;

public sealed class UserOrganizationMembership : ISnapshotable<UserOrganizationMembership>
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string OrganizationUnitId { get; init; } = default!;
    public string? PositionId { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public UserOrganizationMembership Snapshot() => new()
    {
        Id = Id,
        TenantId = TenantId,
        UserId = UserId,
        OrganizationUnitId = OrganizationUnitId,
        PositionId = PositionId,
        IsPrimary = IsPrimary,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
}
