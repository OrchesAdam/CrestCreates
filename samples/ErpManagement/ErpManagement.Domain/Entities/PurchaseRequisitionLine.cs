using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class PurchaseRequisitionLine : AuditedEntity<Guid>
{
    public Guid RequisitionId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }
    public int Position { get; private set; }
    public Guid TenantId { get; private set; }

    protected PurchaseRequisitionLine() { }

    public PurchaseRequisitionLine(Guid id, Guid requisitionId, Guid materialId, decimal quantity, decimal unitPrice, int position, Guid tenantId)
    {
        Id = id;
        RequisitionId = requisitionId;
        MaterialId = materialId;
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("Unit price cannot be negative", nameof(unitPrice));
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = quantity * unitPrice;
        Position = position;
        TenantId = tenantId;
    }
}
