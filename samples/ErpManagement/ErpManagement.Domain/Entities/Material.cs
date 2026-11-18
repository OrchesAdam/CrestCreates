using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class Material : AuditedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Specification { get; private set; }
    public MaterialType MaterialType { get; private set; } = MaterialType.Purchased;
    public Guid? CategoryId { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public Guid? DefaultWarehouseId { get; private set; }
    public decimal SafetyStock { get; private set; }
    public int LeadTimeDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    protected Material() { }

    public Material(Guid id, string code, string name, Guid baseUnitId, Guid tenantId)
    {
        Id = id;
        SetCode(code);
        SetName(name);
        BaseUnitId = baseUnitId;
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
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("Name must be between 1 and 200 characters", nameof(name));
        Name = name;
    }

    public void SetSpecification(string? specification) => Specification = specification;
    public void SetMaterialType(MaterialType type) => MaterialType = type;
    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;
    public void SetBaseUnit(Guid unitId) => BaseUnitId = unitId;
    public void SetDefaultWarehouse(Guid? warehouseId) => DefaultWarehouseId = warehouseId;

    public void SetSafetyStock(decimal safetyStock)
    {
        if (safetyStock < 0)
            throw new ArgumentException("Safety stock cannot be negative", nameof(safetyStock));
        SafetyStock = safetyStock;
    }

    public void SetLeadTimeDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Lead time cannot be negative", nameof(days));
        LeadTimeDays = days;
    }

    public void SetNotes(string? notes) => Notes = notes;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}