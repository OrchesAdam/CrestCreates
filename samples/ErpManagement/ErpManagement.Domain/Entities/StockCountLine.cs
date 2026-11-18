using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockCountLine : AuditedEntity<Guid>
{
    public Guid CountId { get; private set; }
    public Guid MaterialId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal BookQuantity { get; private set; }
    public decimal CountedQuantity { get; private set; }
    public Guid TenantId { get; private set; }

    protected StockCountLine() { }

    public StockCountLine(Guid id, Guid countId, Guid materialId, Guid storageLocationId, decimal bookQuantity, decimal countedQuantity, Guid tenantId)
    {
        Id = id;
        CountId = countId;
        MaterialId = materialId;
        StorageLocationId = storageLocationId;
        BookQuantity = bookQuantity;
        if (countedQuantity < 0) throw new ArgumentException("Counted quantity cannot be negative", nameof(countedQuantity));
        CountedQuantity = countedQuantity;
        TenantId = tenantId;
    }

    public decimal Difference => CountedQuantity - BookQuantity;
}
