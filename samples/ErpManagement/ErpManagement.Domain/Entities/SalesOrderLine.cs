using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SalesOrderLine : AuditedEntity<Guid>
{
    public Guid OrderId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ShippedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }
    public int Position { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid TenantId { get; private set; }

    protected SalesOrderLine() { }

    public SalesOrderLine(Guid id, Guid orderId, Guid materialId, decimal quantity, decimal unitPrice, int position, Guid warehouseId, Guid storageLocationId, Guid tenantId)
    {
        Id = id;
        OrderId = orderId;
        MaterialId = materialId;
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("Unit price cannot be negative", nameof(unitPrice));
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        Position = position;
        WarehouseId = warehouseId;
        StorageLocationId = storageLocationId;
        TenantId = tenantId;
    }

    public void AddShipped(decimal quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Shipped quantity must be positive", nameof(quantity));
        if (ShippedQuantity + quantity > Quantity) throw new InvalidOperationException("Shipped quantity would exceed the ordered quantity.");
        ShippedQuantity += quantity;
    }
}
