using CrestCreates.EventBus.Abstractions;

namespace ErpManagement.Application.Contracts.Events;

public sealed class StockChanged : ILocalEvent
{
    public Guid MaterialId { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid StorageLocationId { get; init; }
    public decimal NewQuantity { get; init; }
    public string MovementType { get; init; } = string.Empty;
    public Guid SourceDocId { get; init; }
}