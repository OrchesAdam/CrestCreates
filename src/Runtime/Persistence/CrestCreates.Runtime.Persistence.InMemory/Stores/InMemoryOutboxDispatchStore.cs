using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryOutboxDispatchStore : IOutboxDispatchStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    public InMemoryOutboxDispatchStore(InMemoryRuntimeTransactionCoordinator coordinator, TimeProvider? timeProvider = null)
    { _coordinator = coordinator; _timeProvider = timeProvider ?? TimeProvider.System; }

    public ValueTask<IReadOnlyList<OutboxDeliveryClaim>> ClaimAsync(OutboxClaimRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync<IReadOnlyList<OutboxDeliveryClaim>>(_ =>
        {
            var now = request.Now ?? _timeProvider.GetUtcNow();
            var state = _coordinator.RequireAmbientState();
            var result = new List<OutboxDeliveryClaim>();
            foreach (var record in state.Outbox.Values.OrderBy(r => r.Message.Metadata.CreatedAt).ThenBy(r => r.Message.Metadata.MessageId, StringComparer.Ordinal))
            {
                if (result.Count >= request.BatchSize) break;
                if (record.Status is OutboxDeliveryStatus.Delivered or OutboxDeliveryStatus.DeadLettered) continue;
                if (record.Status == OutboxDeliveryStatus.InFlight && record.LeaseExpiresAt > now) continue;
                if (record.NextAttemptAt > now) continue;
                record.Status = OutboxDeliveryStatus.InFlight;
                record.Attempt++;
                record.Fence++;
                record.LeaseOwner = request.OwnerId;
                record.LeaseExpiresAt = now + request.LeaseDuration;
                result.Add(ToClaim(record));
            }
            return ValueTask.FromResult<IReadOnlyList<OutboxDeliveryClaim>>(result);
        }, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> AckAsync(string messageId, OutboxDeliveryLease lease, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.Delivered; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = null; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> RetryAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.RetryDue; record.LastFailureCode = failure.Code; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = nextAttemptAt; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.DeadLettered; record.LastFailureCode = failure.Code; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = null; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    private ValueTask<OutboxDeliveryMutationResult> MutateAsync(string messageId, OutboxDeliveryLease lease, Func<InMemoryOutboxRecord, OutboxDeliveryMutationResult> mutate, CancellationToken ct)
        => _coordinator.ExecuteAsync(_ =>
        {
            var state = _coordinator.RequireAmbientState();
            if (!state.Outbox.TryGetValue(messageId, out var record)) return ValueTask.FromResult(OutboxDeliveryMutationResult.NotFound);
            if (!string.Equals(record.LeaseOwner, lease.OwnerId, StringComparison.Ordinal) || record.Fence != lease.Fence || record.Status != OutboxDeliveryStatus.InFlight)
                return ValueTask.FromResult(OutboxDeliveryMutationResult.StaleLease);
            return ValueTask.FromResult(mutate(record));
        }, ct);

    private static OutboxDeliveryClaim ToClaim(InMemoryOutboxRecord record) => new()
    {
        Message = record.Message.Snapshot(), Status = record.Status,
        Lease = new OutboxDeliveryLease { OwnerId = record.LeaseOwner!, ExpiresAt = record.LeaseExpiresAt!.Value, Attempt = record.Attempt, Fence = record.Fence },
        NextAttemptAt = record.NextAttemptAt, LastFailureCode = record.LastFailureCode
    };
}
