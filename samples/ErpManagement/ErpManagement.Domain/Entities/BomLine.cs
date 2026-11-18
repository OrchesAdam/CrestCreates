using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class BomLine : AuditedEntity<Guid>
{
    public Guid BomId { get; private set; }
    public Guid ComponentMaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public int Position { get; private set; }
    public Guid TenantId { get; private set; }

    protected BomLine() { }

    public BomLine(Guid id, Guid bomId, Guid componentMaterialId, decimal quantity, int position, Guid tenantId)
    {
        Id = id;
        BomId = bomId;
        ComponentMaterialId = componentMaterialId;
        SetQuantity(quantity);
        Position = position;
        TenantId = tenantId;
    }

    public void SetQuantity(decimal quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("BOM line quantity must be positive", nameof(quantity));
        Quantity = quantity;
    }

    public void SetPosition(int position) => Position = position;
}