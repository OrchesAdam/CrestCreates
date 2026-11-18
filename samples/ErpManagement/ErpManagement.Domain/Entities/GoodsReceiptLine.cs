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
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive", nameof(quantity));
        Quantity = quantity;
        UnitPrice = unitPrice;
        TenantId = tenantId;
    }
}
