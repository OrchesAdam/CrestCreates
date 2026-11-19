using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;

namespace ErpManagement.Application.Contracts.Events;

[CrestEvent(Id = ErpIntegrationWire.StockPostedId, Name = ErpIntegrationWire.StockPosted,
    Version = ErpIntegrationWire.Version, Scope = EventScope.Integration, RequiresIdempotency = true)]
public sealed record StockPostedEvent : IErpIntegrationEvent
{
    public string MessageId { get; init; } = string.Empty;
    public string EventId { get; init; } = string.Empty;
    string? IErpIntegrationEvent.TenantKey => TenantId;
    DateTime IDomainEvent.OccurredOn => PostedAt.UtcDateTime;
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