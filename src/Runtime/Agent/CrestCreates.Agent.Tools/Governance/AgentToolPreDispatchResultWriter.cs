namespace CrestCreates.Agent.Tools;

/// <summary>
/// Persists the durable outcomes of the pre-dispatch reconciliation protocol:
/// immutable terminal receipts (first-write CAS, replayed on race) and mutable
/// observations (revision-based CAS upsert). Terminal receipt persistence is
/// followed by best-effort accountability publication that can never alter the
/// reconciliation result.
/// </summary>
internal sealed class AgentToolPreDispatchResultWriter
{
    private readonly IAgentToolPreDispatchReconciliationStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IAgentToolPreDispatchReconciliationAccountabilityProducer? _accountabilityProducer;

    public AgentToolPreDispatchResultWriter(
        IAgentToolPreDispatchReconciliationStore store,
        TimeProvider? timeProvider = null,
        IAgentToolPreDispatchReconciliationAccountabilityProducer? accountabilityProducer = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _accountabilityProducer = accountabilityProducer;
    }

    public async ValueTask<AgentToolPreDispatchReconciliationResult> WriteTerminalAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var receipt = new AgentToolPreDispatchReconciliationReceipt
        {
            Identity = identity,
            Status = status,
            ReasonCode = reasonCode,
            TerminalAt = now,
            IntegrityValue = ComputeIntegrity(identity, status, reasonCode, now)
        };

        var inserted = await _store.TryInsertReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
        if (!inserted)
        {
            // CAS insert failed — another reconciler won the race. Read the existing receipt.
            var existingReceipt = await _store.ReadReceiptAsync(identity, cancellationToken).ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                // Return the already-persisted receipt, not a new one.
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = ReplayStatus(existingReceipt),
                    Receipt = existingReceipt
                };
            }

            // CAS failed AND no existing receipt found — the store is in an inconsistent state.
            // The durable terminal form was not established; do not fabricate a protocol result.
            throw new InvalidOperationException(
                $"Terminal receipt insert failed for attempt '{identity.AttemptId}' and no existing " +
                "receipt could be read back; refusing to return an unpersisted terminal result.");
        }

        // Receipt was successfully persisted. Publish accountability best-effort.
        // Accountability failure must NOT alter the reconciliation result.
        if (_accountabilityProducer is not null)
        {
            try
            {
                await _accountabilityProducer.PublishAsync(identity, status, reasonCode ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Accountability failure is observed/logged and cannot alter the reconciliation result.
            }
        }

        return new AgentToolPreDispatchReconciliationResult
        {
            Status = status,
            Receipt = receipt
        };
    }

    public async ValueTask<AgentToolPreDispatchReconciliationResult> WriteObservationAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var existing = await _store.ReadObservationAsync(identity, cancellationToken).ConfigureAwait(false);
        var newRevision = (existing?.Revision ?? 0) + 1;

        var observation = new AgentToolPreDispatchReconciliationObservation
        {
            Identity = identity,
            Status = status,
            ReasonCode = reasonCode,
            ObservedAt = now,
            Revision = newRevision
        };

        // Check observation CAS result — if the upsert failed (concurrent revision change),
        // re-read the durable store and return the winner's state instead of fabricating a result.
        var upserted = await _store.TryUpsertObservationAsync(
            observation,
            existing?.Revision ?? 0,
            cancellationToken).ConfigureAwait(false);
        if (!upserted)
        {
            // Observation CAS miss — another reconciler advanced the revision. Re-read the
            // durable store and return the winner's state instead of fabricating a result.
            var terminalReceipt = await _store.ReadReceiptAsync(identity, cancellationToken).ConfigureAwait(false);
            if (terminalReceipt is not null)
            {
                // The race winner already established a terminal receipt — replay it.
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = ReplayStatus(terminalReceipt),
                    Receipt = terminalReceipt
                };
            }

            var current = await _store.ReadObservationAsync(identity, cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                // Return the current durable observation (still-pending), not the lost write.
                return new AgentToolPreDispatchReconciliationResult
                {
                    Status = current.Status,
                    Observation = current
                };
            }

            // Neither a terminal receipt nor an observation could be read back — the durable
            // form is missing. Do not fabricate a protocol result.
            throw new InvalidOperationException(
                $"Observation CAS upsert failed for attempt '{identity.AttemptId}' and no durable " +
                "terminal receipt or observation could be read back; refusing to return an unpersisted result.");
        }

        return new AgentToolPreDispatchReconciliationResult
        {
            Status = status,
            Observation = observation
        };
    }

    internal static bool IsTerminal(AgentToolPreDispatchReconciliationStatus status)
        => status is AgentToolPreDispatchReconciliationStatus.Released
            or AgentToolPreDispatchReconciliationStatus.Conflict
            or AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown;

    internal static AgentToolPreDispatchReconciliationStatus ReplayStatus(
        AgentToolPreDispatchReconciliationReceipt receipt)
        => receipt.Status == AgentToolPreDispatchReconciliationStatus.Released
            ? AgentToolPreDispatchReconciliationStatus.AlreadyReleased
            : receipt.Status;

    internal static string ComputeIntegrity(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string? reasonCode,
        DateTimeOffset terminalAt)
        => $"{identity.AttemptId}:{identity.LogicalInvocationKey.InvocationId}:{status}:{reasonCode}:{terminalAt:O}";
}
