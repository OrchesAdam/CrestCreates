using System.Collections.Concurrent;

namespace CrestCreates.Agent.Tools;

internal sealed class DevelopmentInMemoryAgentToolPreDispatchReconciliationStore : IAgentToolPreDispatchReconciliationStore
{
    private readonly ConcurrentDictionary<AgentToolPreDispatchIdentity, AgentToolPreDispatchReconciliationObservation> _observations = new();
    private readonly ConcurrentDictionary<AgentToolPreDispatchIdentity, AgentToolPreDispatchReconciliationReceipt> _receipts = new();
    private readonly object _lock = new();

    public ValueTask<AgentToolPreDispatchReconciliationObservation?> ReadObservationAsync(
        AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
    {
        _observations.TryGetValue(identity, out var observation);
        return new(observation);
    }

    public ValueTask<bool> TryUpsertObservationAsync(
        AgentToolPreDispatchReconciliationObservation observation, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_receipts.ContainsKey(observation.Identity))
                return new(false);

            if (!_observations.TryGetValue(observation.Identity, out var existing))
            {
                if (expectedRevision != 0)
                    return new(false);
                _observations[observation.Identity] = observation;
                return new(true);
            }

            if (existing.Revision != expectedRevision)
                return new(false);

            _observations[observation.Identity] = observation with { Revision = existing.Revision + 1 };
            return new(true);
        }
    }

    public ValueTask<AgentToolPreDispatchReconciliationReceipt?> ReadReceiptAsync(
        AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
    {
        _receipts.TryGetValue(identity, out var receipt);
        return new(receipt);
    }

    public ValueTask<bool> TryInsertReceiptAsync(
        AgentToolPreDispatchReconciliationReceipt receipt, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_receipts.TryAdd(receipt.Identity, receipt))
            {
                _observations.TryRemove(receipt.Identity, out _);
                return new(false);
            }

            // A terminal receipt supersedes the mutable retry observation. Keeping
            // both makes cleanup and later readers disagree about whether this
            // identity is still retryable.
            _observations.TryRemove(receipt.Identity, out _);
            return new(true);
        }
    }
}
