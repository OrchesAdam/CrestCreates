using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Organization.Abstractions;

public sealed class OrganizationUnit : ISnapshotable<OrganizationUnit>
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string Name { get; init; } = default!;
    public string? Code { get; init; }
    public string? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public OrganizationUnit Snapshot() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Name = Name,
        Code = Code,
        ParentId = ParentId,
        SortOrder = SortOrder,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
}
