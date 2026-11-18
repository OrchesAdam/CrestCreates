using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Json;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Application.Events;

/// <summary>
/// Appends the StockPosted integration event to the transactional outbox. The MessageId is a
/// stable idempotency key per receipt so a resend after a failed emission window converges.
/// The caller marks <c>StockEventEmittedAt</c> and saves the receipt after this returns.
/// </summary>
public sealed class StockPostedEventPublisher(
    IOutboxMessageFactory messageFactory,
    ITransactionalOutboxWriter outboxWriter,
    IRuntimeTransactionCoordinator transactionCoordinator)
{
    public async Task PublishAsync(
        GoodsReceipt receipt,
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = receipt.TenantId == Guid.Empty ? null : receipt.TenantId.ToString();
        var payload = new StockPostedEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ReceiptId = receipt.Id,
            PurchaseOrderId = purchaseOrder.Id,
            SupplierId = receipt.SupplierId,
            TenantId = tenantId,
            Lines = receipt.Lines.Select(line => new StockPostedLine
            {
                MaterialId = line.MaterialId,
                WarehouseId = receipt.WarehouseId,
                StorageLocationId = line.StorageLocationId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            }).ToList(),
            PostedAt = receipt.PostedAt ?? now
        };

        var metadata = new OutboxMessageMetadata
        {
            MessageId = $"erp.stock-posted:{receipt.Id}",
            TenantId = tenantId,
            ContractId = ErpContractIds.StockPostedContractId,
            PayloadTypeId = ErpContractIds.StockPostedContractId,
            EventName = "StockPosted",
            EventVersion = 1,
            RequiredConsumerIds = new[] { ErpContractIds.StockPostedConsumerId },
            OccurredAt = now,
            CreatedAt = now,
            CorrelationId = receipt.Id.ToString()
        };

        await transactionCoordinator.ExecuteAsync(async innerCt =>
        {
            await outboxWriter.AppendAsync(
                messageFactory.Create(metadata, payload, ErpEventJsonContext.Default.StockPostedEvent),
                innerCt).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}