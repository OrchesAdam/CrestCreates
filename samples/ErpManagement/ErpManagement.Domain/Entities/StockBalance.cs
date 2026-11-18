using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockBalance : AuditedEntity<Guid>
{
    public Guid MaterialId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid TenantId { get; private set; }

    protected StockBalance() { }

    public StockBalance(Guid id, Guid materialId, Guid warehouseId, Guid storageLocationId, Guid tenantId)
    {
        Id = id;
        MaterialId = materialId;
        WarehouseId = warehouseId;
        StorageLocationId = storageLocationId;
        TenantId = tenantId;
    }

    internal decimal ApplyMovement(decimal delta, bool allowNegative)
    {
        var next = Quantity + delta;
        if (!allowNegative && next < 0)
            throw new InvalidOperationException($"Insufficient stock for material {MaterialId} at location {StorageLocationId}.");
        Quantity = next;
        return Quantity;
    }
}
