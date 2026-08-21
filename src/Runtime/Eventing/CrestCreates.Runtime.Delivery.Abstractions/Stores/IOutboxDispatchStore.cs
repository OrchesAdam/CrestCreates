namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public interface IOutboxDispatchStore
{
    ValueTask<IReadOnlyList<OutboxDeliveryClaim>> ClaimAsync(OutboxClaimRequest request, CancellationToken cancellationToken = default);
    ValueTask<OutboxDeliveryMutationResult> AckAsync(string messageId, OutboxDeliveryLease lease, CancellationToken cancellationToken = default);
    ValueTask<OutboxDeliveryMutationResult> RetryAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default);
    ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, CancellationToken cancellationToken = default);
}
