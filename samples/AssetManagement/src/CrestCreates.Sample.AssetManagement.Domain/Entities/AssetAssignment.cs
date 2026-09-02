using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Sample.AssetManagement.Domain.Entities;

[Entity]
public sealed class AssetAssignment : MustHaveTenantOrganizationEntity<Guid>
{
    public Guid AssetId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime AssignedAt { get; private set; }
    public DateTime? ReturnedAt { get; private set; }
    public bool IsActive => ReturnedAt is null;

    private AssetAssignment() { }

    public AssetAssignment(Guid id, string tenantId, Guid assetId, string userId, Guid organizationId)
    {
        Id = id;
        TenantId = tenantId;
        AssetId = assetId;
        UserId = userId;
        OrganizationId = organizationId;
        AssignedAt = DateTime.UtcNow;
    }

    public void MarkReturned() => ReturnedAt ??= DateTime.UtcNow;
}
