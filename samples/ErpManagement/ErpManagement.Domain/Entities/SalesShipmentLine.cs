using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SalesShipmentLine : AuditedEntity<Guid>
{
    public Guid ShipmentId { get; private set; }
    public Guid SalesOrderLineId { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public Guid TenantId { get; private set; }

    protected SalesShipmentLine() { }

    public SalesShipmentLine(Guid id, Guid shipmentId, Guid salesOrderLineId, Guid materialId, Guid storageLocationId, decimal quantity, decimal unitPrice, Guid tenantId)
    {
        Id = id;
        ShipmentId = shipmentId;
        SalesOrderLineId = salesOrderLineId;
        MaterialId = materialId;
        StorageLocationId = storageLocationId;
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive", nameof(quantity));
        Quantity = quantity;
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        UnitPrice = unitPrice;
        TenantId = tenantId;
    }
}
