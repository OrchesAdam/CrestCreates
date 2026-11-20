using System.Text.Json;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ErpManagement.Tests;

public sealed class ErpTransportTests
{
    [Fact]
    public async Task StockOutboxPreservesTheDeliveryKeyAndWaitsForConfirmation()
    {
        var receiptId = Guid.NewGuid();
        var deliveryKey = $"erp.stock-posted:{receiptId:D}";
        var transport = new PendingTransport();
        var handler = new ErpStockPostedOutboxHandler(transport);
        var delivery = handler.HandleAsync(CreateDelivery(receiptId, deliveryKey)).AsTask();

        Assert.False(delivery.IsCompleted);
        Assert.Equal(deliveryKey, Assert.IsType<StockPostedEvent>(transport.Payload).MessageId);
        transport.Confirmation.SetResult();

        Assert.Equal(OutboxDeliveryOutcome.Accepted, await delivery);
    }

    [Fact]
    public async Task StockOutboxPropagatesAnUnconfirmedPublish()
    {
        var receiptId = Guid.NewGuid();
        var transport = new PendingTransport();
        var handler = new ErpStockPostedOutboxHandler(transport);
        var delivery = handler.HandleAsync(CreateDelivery(receiptId, $"erp.stock-posted:{receiptId:D}")).AsTask();
        transport.Confirmation.SetException(new InvalidOperationException("No confirm"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery);
    }

    [Fact]
    public async Task InboxDetectsSameKeyWithChangedPayloadAndPropagatesDatabaseErrors()
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(x => x.Change(null)).Returns(Mock.Of<IDisposable>());
        var store = new TestInbox();
        var consumer = new ErpStockPostedInboxConsumer(tenant.Object, Mock.Of<ITenantProvider>(), store,
            NullLogger<ErpStockPostedInboxConsumer>.Instance);
        var payload = new StockPostedEvent { MessageId = "test-key", ReceiptId = Guid.NewGuid() };

        await consumer.ConsumeAsync(payload);
        await consumer.ConsumeAsync(payload with { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAsync(payload with { SupplierId = Guid.NewGuid() }));
        Assert.Matches("^[a-f0-9]{64}$", store.PayloadHash!);

        store.Failure = new TimeoutException("PostgreSQL unavailable");
        await Assert.ThrowsAsync<TimeoutException>(() => consumer.ConsumeAsync(payload));
    }

    private static OutboxDeliveryContext CreateDelivery(Guid receiptId, string deliveryKey)
    {
        var now = DateTimeOffset.UnixEpoch;
        var metadata = new OutboxMessageMetadata
        {
            MessageId = deliveryKey,
            ContractId = ErpIntegrationWire.StockPostedId,
            PayloadTypeId = ErpIntegrationWire.StockPosted,
            EventName = ErpIntegrationWire.StockPosted,
            RequiredConsumerIds = [],
            CreatedAt = now,
            OccurredAt = now
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new StockPostedEvent { ReceiptId = receiptId, PostedAt = now, MessageId = deliveryKey },
            ErpIntegrationWire.GetJsonTypeInfo<StockPostedEvent>());
        return new OutboxDeliveryContext
        {
            Message = new OutboxMessage
            {
                Metadata = metadata,
                Payload = payload,
                Integrity = OutboxMessageIntegrity.Compute(metadata, payload)
            },
            Lease = new OutboxDeliveryLease { OwnerId = "test", ExpiresAt = now, Attempt = 1, Fence = 1 },
            AttemptDeadline = now,
            Services = Mock.Of<IServiceProvider>()
        };
    }

    private sealed class PendingTransport : IErpIntegrationTransport
    {
        public TaskCompletionSource Confirmation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IErpIntegrationEvent? Payload { get; private set; }

        public Task PublishAsync(IErpIntegrationEvent payload, CancellationToken cancellationToken)
        {
            Payload = payload;
            return Confirmation.Task;
        }
    }

    private sealed class TestInbox : IErpIntegrationInbox
    {
        public string? PayloadHash { get; private set; }
        public Exception? Failure { get; set; }

        public Task<ErpInboxWriteResult> RecordAsync(string consumerId, string messageId, string contractId, Guid tenantId,
            string payloadHash, CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
                return Task.FromException<ErpInboxWriteResult>(failure);

            if (PayloadHash is { } recorded)
                return Task.FromResult(recorded == payloadHash ? ErpInboxWriteResult.Duplicate : ErpInboxWriteResult.Conflict);

            PayloadHash = payloadHash;
            return Task.FromResult(ErpInboxWriteResult.Inserted);
        }
    }
}
