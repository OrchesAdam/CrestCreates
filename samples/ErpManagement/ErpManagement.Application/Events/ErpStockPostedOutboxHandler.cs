using System.Text.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Json;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Application.Events;

/// <summary>
/// Contract-level outbox delivery handler for StockPostedEvent. The dispatcher routes by contract id;
/// AddOutboxRequiredConsumer only registers the consumer/resolver, so a custom contract needs this
/// handler to decode the source-generated payload and invoke each required consumer.
/// </summary>
public sealed class ErpStockPostedOutboxHandler : IOutboxDeliveryHandler
{
    private readonly IOutboxRequiredConsumerResolver<StockPostedEvent> _resolver;

    public ErpStockPostedOutboxHandler(IOutboxRequiredConsumerResolver<StockPostedEvent> resolver)
        => _resolver = resolver;

    public string ContractId => ErpContractIds.StockPostedContractId;

    public async ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Deserialize(context.Message.Payload, ErpEventJsonContext.Default.StockPostedEvent)
                      ?? throw new InvalidOperationException("StockPosted payload was empty or invalid.");
        foreach (var consumerId in context.Message.Metadata.RequiredConsumerIds.Order(StringComparer.Ordinal))
        {
            var consumer = _resolver.Resolve(context.Services, consumerId);
            var result = await consumer.ConsumeAsync(payload, context, cancellationToken).ConfigureAwait(false);
            if (result.Outcome is OutboxDeliveryOutcome.Retry or OutboxDeliveryOutcome.Conflict)
                return result.Outcome;
        }
        return OutboxDeliveryOutcome.Accepted;
    }
}