namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public interface IOutboxDeliveryHandler
{
    string ContractId { get; }
    ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default);
}
