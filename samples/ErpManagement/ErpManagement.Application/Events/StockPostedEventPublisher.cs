using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Application.Events;

public interface IErpOutboxAppender
{
    ValueTask AppendAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public interface IErpDeliveryStatusReader
{
    Task<ErpDeliveryStatus?> GetAsync(string messageId, string consumerId, CancellationToken cancellationToken);
}

public sealed record ErpDeliveryStatus(string Status, int AttemptCount, bool Consumed, string? FailureCode);

public sealed class StockPostedEventPublisher(IOutboxMessageFactory messageFactory, IErpOutboxAppender appender)
{
    public async Task PublishAsync(
        GoodsReceipt receipt,
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken = default)
    {
        var postedAt = receipt.PostedAt ?? throw new InvalidOperationException("The receipt has not been posted.");
        var messageId = $"erp.stock-posted:{receipt.Id}";
        var tenantId = receipt.TenantId == Guid.Empty ? null : receipt.TenantId.ToString();
        var payload = new StockPostedEvent
        {
            MessageId = messageId,
            EventId = receipt.Id.ToString("N"),
            ReceiptId = receipt.Id,
            PurchaseOrderId = purchaseOrder.Id,
            SupplierId = receipt.SupplierId,
            TenantId = tenantId,
            Lines = receipt.Lines.OrderBy(line => line.Id).Select(line => new StockPostedLine
            {
                MaterialId = line.MaterialId,
                WarehouseId = receipt.WarehouseId,
                StorageLocationId = line.StorageLocationId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            }).ToList(),
            PostedAt = postedAt
        };

        await AppendAsync(payload, ErpIntegrationWire.StockPostedId, ErpIntegrationWire.StockPosted,
            receipt.Id, cancellationToken);
    }

    public ValueTask AppendAsync<TEvent>(TEvent payload, string contractId, string eventName,
        Guid correlationId, CancellationToken cancellationToken) where TEvent : IErpIntegrationEvent
    {
        var occurredAt = new DateTimeOffset(payload.OccurredOn);
        var metadata = new OutboxMessageMetadata
        {
            MessageId = payload.MessageId,
            TenantId = payload.TenantKey,
            ContractId = contractId,
            PayloadTypeId = contractId,
            EventName = eventName,
            EventVersion = ErpIntegrationWire.Version,
            RequiredConsumerIds = [],
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            CorrelationId = correlationId.ToString()
        };

        return appender.AppendAsync(messageFactory.Create(metadata, payload, ErpIntegrationWire.GetJsonTypeInfo<TEvent>()),
            cancellationToken);
    }
}