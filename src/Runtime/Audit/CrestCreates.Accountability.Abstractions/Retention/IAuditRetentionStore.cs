namespace CrestCreates.Accountability.Abstractions.Retention;

/// <summary>
/// Provider-side retention protocol. Implementations own the atomic facts: a batch is visible
/// only when its body deletions, ledger transitions, and receipt committed together. The store
/// reloads the persisted run and never trusts per-batch cutoff or budget arguments.
/// </summary>
public interface IAuditRetentionStore
{
    AuditRetentionStoreIdentity Identity { get; }

    /// <summary>
    /// Creates the run once per (store, sink, scope, request id). An existing run with the same
    /// policy and reason binding is returned as-is; a different binding is a conflict.
    /// </summary>
    ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
        AuditRetentionRunRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AuditRetentionPreview> PreviewAsync(
        AuditRetentionPreviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes one bounded batch. A repeated (run, ordinal) returns the committed receipt and
    /// never deletes another batch. A different binding for the same key is a conflict.
    /// </summary>
    ValueTask<AuditRetentionBatchOutcome> ExecuteBatchAsync(
        AuditRetentionBatchRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AuditRetentionRunRecord?> ReadRunAsync(
        AuditRetentionRunKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the immutable receipt of one committed batch. Returns null when the ordinal has
    /// not committed. This is the single source for replay, reconciliation and re-export.
    /// </summary>
    ValueTask<AuditRetentionBatchReceipt?> ReadBatchAsync(
        AuditRetentionRunKey key,
        long batchOrdinal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminal transition for a run the kernel stopped outside a batch receipt (cancel or
    /// failure). An already terminal run is returned unchanged.
    /// </summary>
    ValueTask<AuditRetentionRunRecord> FinalizeRunAsync(
        AuditRetentionRunFinalization finalization,
        CancellationToken cancellationToken = default);

    ValueTask<AuditHold> PlaceHoldAsync(
        AuditHoldPlacement placement,
        CancellationToken cancellationToken = default);

    ValueTask<AuditHold> ReleaseHoldAsync(
        AuditHoldRelease release,
        CancellationToken cancellationToken = default);
}
