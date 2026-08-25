using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
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
            var now = _timeProvider.GetUtcNow();
            var state = _coordinator.RequireAmbientState();
            if (request.SupportedContractIds is not null && request.SupportedRequiredConsumerIds is not null)
            {
                foreach (var record in state.Outbox.Values.Where(record => record.Status is not (OutboxDeliveryStatus.Delivered or OutboxDeliveryStatus.DeadLettered)))
                {
                    if (!request.SupportedContractIds.Contains(record.Message.Metadata.ContractId))
                        throw new OutboxCompositionException($"Outbox contract '{record.Message.Metadata.ContractId}' is not registered.");
                    foreach (var consumerId in record.Message.Metadata.RequiredConsumerIds)
                        if (!request.SupportedRequiredConsumerIds.Contains(consumerId))
                            throw new OutboxCompositionException($"Outbox required consumer '{consumerId}' is not registered.");
                }
            }
            var result = new List<OutboxDeliveryClaim>();
            foreach (var record in state.Outbox.Values
                .OrderBy(r => r.NextAttemptAt ?? r.LeaseExpiresAt ?? DateTimeOffset.MinValue)
                .ThenBy(r => r.Message.Metadata.OccurredAt)
                .ThenBy(r => r.Message.Metadata.MessageId, StringComparer.Ordinal))
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

    public ValueTask<DateTimeOffset> GetProviderUtcNowAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_timeProvider.GetUtcNow());

    public ValueTask<OutboxDeliveryMutationResult> AckAsync(string messageId, OutboxDeliveryLease lease, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.Delivered; record.TerminalLeaseOwner = lease.OwnerId; record.TerminalFence = lease.Fence; record.TerminalFailureCode = null; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = null; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> RetryAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.Pending; record.LastFailureCode = failure.Code; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = nextAttemptAt; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    public ValueTask<OutboxDeliveryMutationResult> DeadLetterAsync(string messageId, OutboxDeliveryLease lease, OutboxDeliveryFailure failure, CancellationToken cancellationToken = default)
        => MutateAsync(messageId, lease, record => { record.Status = OutboxDeliveryStatus.DeadLettered; record.LastFailureCode = failure.Code; record.TerminalLeaseOwner = lease.OwnerId; record.TerminalFence = lease.Fence; record.TerminalFailureCode = failure.Code; record.LeaseOwner = null; record.LeaseExpiresAt = null; record.NextAttemptAt = null; return OutboxDeliveryMutationResult.Applied; }, cancellationToken);

    private ValueTask<OutboxDeliveryMutationResult> MutateAsync(string messageId, OutboxDeliveryLease lease, Func<InMemoryOutboxRecord, OutboxDeliveryMutationResult> mutate, CancellationToken ct)
        => _coordinator.ExecuteAsync(_ =>
        {
            var state = _coordinator.RequireAmbientState();
            if (!state.Outbox.TryGetValue(messageId, out var record)) return ValueTask.FromResult(OutboxDeliveryMutationResult.NotFound);
            if (record.Status is OutboxDeliveryStatus.Delivered or OutboxDeliveryStatus.DeadLettered)
            {
                if (string.Equals(record.TerminalLeaseOwner, lease.OwnerId, StringComparison.Ordinal) && record.TerminalFence == lease.Fence)
                    return ValueTask.FromResult(OutboxDeliveryMutationResult.AlreadyApplied);
                return ValueTask.FromResult(OutboxDeliveryMutationResult.StaleFence);
            }
            if (record.Fence != lease.Fence)
                return ValueTask.FromResult(OutboxDeliveryMutationResult.StaleFence);
            if (!string.Equals(record.LeaseOwner, lease.OwnerId, StringComparison.Ordinal)
                || record.Status != OutboxDeliveryStatus.InFlight
                || record.LeaseExpiresAt is null
                || record.LeaseExpiresAt <= _timeProvider.GetUtcNow())
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
