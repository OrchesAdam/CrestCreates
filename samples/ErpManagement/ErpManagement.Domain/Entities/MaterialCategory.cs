using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class MaterialCategory : AuditedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public Guid TenantId { get; private set; }

    protected MaterialCategory() { }

    public MaterialCategory(Guid id, string code, string name, Guid tenantId)
    {
        Id = id;
        SetCode(code);
        SetName(name);
        TenantId = tenantId;
    }

    public void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            throw new ArgumentException("Code must be between 1 and 64 characters", nameof(code));
        Code = code;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ArgumentException("Name must be between 1 and 100 characters", nameof(name));
        Name = name;
    }

    public void SetParent(Guid? parentId) => ParentId = parentId;
    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}