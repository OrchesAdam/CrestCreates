using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Sample.AssetManagement.Domain.Entities;

[Entity]
public sealed class AssetCategory : MustHaveTenantOrganizationEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;

    private AssetCategory() { }

    public AssetCategory(Guid id, string tenantId, string name)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));
    }
}
