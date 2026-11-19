using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class GoodsReceiptLine : AuditedEntity<Guid>
{
    public Guid ReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public Guid TenantId { get; private set; }

    protected GoodsReceiptLine() { }

    public GoodsReceiptLine(Guid id, Guid receiptId, Guid purchaseOrderLineId, Guid materialId, Guid storageLocationId, decimal quantity, decimal unitPrice, Guid tenantId)
    {
        Id = id;
        ReceiptId = receiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        MaterialId = materialId;
        StorageLocationId = storageLocationId;
        if (quantity <= 0 || decimal.Round(quantity, 4) != quantity)
            throw new ArgumentException("Quantity must be positive with at most four decimal places.", nameof(quantity));
        if (unitPrice < 0 || decimal.Round(unitPrice, 2) != unitPrice)
            throw new ArgumentException("Unit price must be non-negative with at most two decimal places.", nameof(unitPrice));
        Quantity = quantity;
        UnitPrice = unitPrice;
        TenantId = tenantId;
    }
}
