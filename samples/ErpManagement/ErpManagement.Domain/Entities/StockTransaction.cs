using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockTransaction : AuditedEntity<Guid>
{
    public Guid MaterialId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal SignedQuantity { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public string SourceDocType { get; private set; } = string.Empty;
    public Guid SourceDocId { get; private set; }
    public Guid? SourceOperationId { get; private set; }
    public Guid TenantId { get; private set; }

    protected StockTransaction() { }

    public StockTransaction(Guid id, Guid materialId, Guid warehouseId, Guid storageLocationId, decimal signedQuantity, decimal balanceAfter, StockMovementType movementType, string sourceDocType, Guid sourceDocId, Guid tenantId, Guid? sourceOperationId = null)
    {
        Id = id;
        MaterialId = materialId;
        WarehouseId = warehouseId;
        StorageLocationId = storageLocationId;
        if (signedQuantity == 0) throw new ArgumentException("Transaction quantity cannot be zero", nameof(signedQuantity));
        SignedQuantity = signedQuantity;
        BalanceAfter = balanceAfter;
        MovementType = movementType;
        ValidateSource(sourceDocType, sourceDocId);
        SourceDocType = sourceDocType;
        SourceDocId = sourceDocId;
        SourceOperationId = sourceOperationId;
        TenantId = tenantId;
    }

    internal static void ValidateSource(string sourceDocType, Guid sourceDocId)
    {
        if (string.IsNullOrWhiteSpace(sourceDocType) || sourceDocType.Length > 32)
            throw new ArgumentException("Source document type must be 1..32 characters.", nameof(sourceDocType));
        if (sourceDocId == Guid.Empty)
            throw new ArgumentException("Source document ID is required.", nameof(sourceDocId));
    }
}
