namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public interface IOutboxRequiredConsumer<in TPayload>
{
    string ConsumerId { get; }
    ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(TPayload payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default);
}
