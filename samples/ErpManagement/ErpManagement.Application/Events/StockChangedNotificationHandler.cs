using CrestCreates.EventBus.Abstractions;
using ErpManagement.Application.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Events;

/// <summary>
/// In-process notification side of a stock posting. Durable cross-boundary delivery is the
/// outbox StockPosted event; this handler only observes the committed movement for logs/alerts.
/// </summary>
public sealed class StockChangedNotificationHandler(
    ILogger<StockChangedNotificationHandler> logger) : ILocalEventHandler<StockChanged>
{
    public Task HandleAsync(StockChanged @event, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Stock changed: material {MaterialId} at warehouse {WarehouseId}/{StorageLocationId} -> {NewQuantity} ({MovementType}, source {SourceDocId})",
            @event.MaterialId,
            @event.WarehouseId,
            @event.StorageLocationId,
            @event.NewQuantity,
            @event.MovementType,
            @event.SourceDocId);
        return Task.CompletedTask;
    }
}