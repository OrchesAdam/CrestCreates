using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;

namespace CrestCreates.HumanTask;

internal sealed class HumanTaskCompletedOutboxHandler : IOutboxDeliveryHandler
{
    private readonly IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> _resolver;

    public HumanTaskCompletedOutboxHandler(IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> resolver)
        => _resolver = resolver;

    public string ContractId => HumanTaskDeliveryConstants.CompletedContractId;

    public async ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize(
                context.Message.Payload,
                HumanTaskJsonSerializerContext.Default.HumanTaskCompletedEvent)
            ?? throw new InvalidOperationException("HumanTask completion payload was empty or invalid.");
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
