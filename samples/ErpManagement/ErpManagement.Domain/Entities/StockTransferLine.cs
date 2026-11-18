using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockTransferLine : AuditedEntity<Guid>
{
    public Guid TransferId { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid FromWarehouseId { get; private set; }
    public Guid FromStorageLocationId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public Guid ToStorageLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid TenantId { get; private set; }

    protected StockTransferLine() { }

    public StockTransferLine(Guid id, Guid transferId, Guid materialId, Guid fromWarehouseId, Guid fromStorageLocationId, Guid toWarehouseId, Guid toStorageLocationId, decimal quantity, Guid tenantId)
    {
        Id = id;
        TransferId = transferId;
        MaterialId = materialId;
        FromWarehouseId = fromWarehouseId;
        FromStorageLocationId = fromStorageLocationId;
        ToWarehouseId = toWarehouseId;
        ToStorageLocationId = toStorageLocationId;
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive", nameof(quantity));
        Quantity = quantity;
        TenantId = tenantId;
    }
}
