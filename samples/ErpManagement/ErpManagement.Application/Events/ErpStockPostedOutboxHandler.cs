using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using ErpManagement.Application.Contracts.Events;

namespace ErpManagement.Application.Events;

public interface IErpIntegrationTransport
{
    Task PublishAsync(IErpIntegrationEvent payload, CancellationToken cancellationToken);
}

public sealed class ErpStockPostedOutboxHandler(IErpIntegrationTransport transport)
    : ErpIntegrationOutboxHandler<StockPostedEvent>(transport, ErpIntegrationWire.StockPostedId, ErpIntegrationWire.StockPosted)
{
}

public class ErpIntegrationOutboxHandler<TEvent>(IErpIntegrationTransport transport, string contractId, string eventName)
    : IOutboxDeliveryHandler where TEvent : class, IErpIntegrationEvent
{
    public string ContractId => contractId;

    public async ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        var message = context.Message;
        var metadata = message.Metadata;
        if (metadata.ContractId != contractId || metadata.PayloadTypeId != eventName || metadata.EventName != eventName ||
            metadata.EventVersion != ErpIntegrationWire.Version || metadata.RequiredConsumerIds.Count != 0)
            return OutboxDeliveryOutcome.Conflict;

        TEvent? payload;
        try
        {
            payload = JsonSerializer.Deserialize(message.Payload, ErpIntegrationWire.GetJsonTypeInfo<TEvent>());
        }
        catch (JsonException)
        {
            return OutboxDeliveryOutcome.Conflict;
        }

        if (payload is null || payload.TenantKey != metadata.TenantId || payload.MessageId != metadata.MessageId)
            return OutboxDeliveryOutcome.Conflict;

        await transport.PublishAsync(payload, cancellationToken).ConfigureAwait(false);
        return OutboxDeliveryOutcome.Accepted;
    }
}
