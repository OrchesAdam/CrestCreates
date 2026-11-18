namespace ErpManagement.Application.Contracts.Events;

public sealed class StockPostedEvent
{
    public string EventId { get; init; } = string.Empty;
    public Guid ReceiptId { get; init; }
    public Guid PurchaseOrderId { get; init; }
    public Guid? SupplierId { get; init; }
    public string? TenantId { get; init; }
    public IReadOnlyList<StockPostedLine> Lines { get; init; } = Array.Empty<StockPostedLine>();
    public DateTimeOffset PostedAt { get; init; }
}

public sealed class StockPostedLine
{
    public Guid MaterialId { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid StorageLocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}