using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Retention;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Accountability.Retention;

public sealed class AuditRetentionExecutor : IAuditRetentionService
{
    private static readonly TimeSpan ReceiptReconciliationTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyDictionary<string, IAuditRetentionStore> _stores;
    private readonly IAuditRecorder? _recorder;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditRetentionExecutor> _logger;

    public AuditRetentionExecutor(
        IEnumerable<IAuditRetentionStore> stores,
        IAuditRecorder? recorder = null,
        TimeProvider? timeProvider = null,
        ILogger<AuditRetentionExecutor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var map = new Dictionary<string, IAuditRetentionStore>(StringComparer.Ordinal);
        foreach (var store in stores)
        {
            ArgumentNullException.ThrowIfNull(store);
            store.Identity.EnsureValid();
            if (!map.TryAdd(store.Identity.StoreId, store))
                throw new AuditRetentionException(
                    AuditRetentionErrorCodes.StoreDuplicate,
                    $"Duplicate audit retention store id '{store.Identity.StoreId}'.");
        }

        _stores = map;
        _recorder = recorder;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AuditRetentionExecutor>.Instance;
    }

    public AuditRetentionStoreIdentity GetStoreIdentity(string storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId) || !_stores.TryGetValue(storeId, out var store))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.StoreUnknown,
                $"Audit retention store '{storeId}' is not registered.");
        }

        if (!store.Identity.Capabilities.IsComplete)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.CapabilityMissing,
                $"Audit retention store '{storeId}' does not declare the full retention capability set.");
        }

        return store.Identity;
    }

    public async ValueTask<AuditRetentionPreview> PreviewAsync(
        AuditRetentionPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var store = ResolveStore(request.StoreId, request.SinkId);
        var preview = await store.PreviewAsync(request, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(preview.PolicyHash, request.Policy.ComputeHash(), StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.PolicyHashMismatch,
                "The store preview policy hash does not match the admitted policy.");
        }

        return preview;
    }

    public async ValueTask<AuditRetentionRunResult> ExecuteAsync(
        AuditRetentionRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var policyHash = request.Policy.ComputeHash();
        if (request.ExpectedPolicyHash is not null
            && !string.Equals(request.ExpectedPolicyHash, policyHash, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.PolicyHashMismatch,
                "The expected policy hash does not match the admitted retention policy.");
        }

        var store = ResolveStore(request.StoreId, request.SinkId);
        var run = await CreateOrReadRunAsync(store, request, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(run.PolicyHash, policyHash, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.PolicyHashMismatch,
                "The persisted run was created with a different policy.");
        }

        // The create-fault recovery read is evidence, not admission: the same-key binding must
        // still hold, or a conflicting retry would execute a run admitted for another reason.
        if (!string.Equals(run.ReasonCode, request.ReasonCode, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.RequestConflict,
                "The audit retention request id is already bound to a different reason code.");
        }

        if (run.State is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
        {
            return StoredResult(run);
        }

        var lastDeleted = run.CommittedDeletedCount;
        var lastBatches = run.CommittedBatchCount;
        var startedAt = _timeProvider.GetUtcNow();
        AuditRetentionMirrorOutcome? firstMirrorFailure = null;
        AuditRetentionMirrorOutcome? lastMirror = null;
        AuditRetentionMirrorOutcome? AggregateMirror() => firstMirrorFailure ?? lastMirror;

        void TrackMirrorFailure(AuditRetentionMirrorOutcome mirror)
        {
            lastMirror = mirror;
            if (mirror.IsFailure && firstMirrorFailure is null)
                firstMirrorFailure = mirror;
        }

        async ValueTask TrackMirrorAsync(AuditRetentionRunRecord currentRun, AuditRetentionBatchReceipt receipt)
        {
            var mirror = await MirrorBatchAsync(currentRun, receipt).ConfigureAwait(false);
            if (mirror is not null)
                TrackMirrorFailure(mirror);
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lastBatches >= request.Policy.MaxBatchesPerRun
                    || _timeProvider.GetUtcNow() - startedAt >= request.Policy.MaxRunDuration)
                {
                    var finalized = await TryFinalizeAsync(
                        store,
                        request.Key,
                        AuditRetentionRunState.BudgetReached,
                        AuditRetentionStopReasons.BudgetReached).ConfigureAwait(false);
                    return finalized is null
                        ? Result(
                            run,
                            lastDeleted,
                            lastBatches,
                            AuditRetentionRunState.BudgetReached,
                            AuditRetentionCommitKnowledge.Committed,
                            remainingMayExist: true,
                            AuditRetentionStopReasons.BudgetReached,
                            AggregateMirror())
                        : Result(
                            finalized,
                            finalized.CommittedDeletedCount,
                            finalized.CommittedBatchCount,
                            finalized.State,
                            AuditRetentionCommitKnowledge.Committed,
                            remainingMayExist: finalized.State != AuditRetentionRunState.Completed,
                            finalized.StopReason,
                            AggregateMirror());
                }

                var ordinal = lastBatches + 1;
                AuditRetentionBatchOutcome batch;
                try
                {
                    batch = await store.ExecuteBatchAsync(
                        new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = ordinal },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (AuditRetentionException exception) when (exception.Code == AuditRetentionErrorCodes.RunNotExecutable)
                {
                    // Another executor sharing the request id finalized the run while this loop
                    // was queued. The persisted terminal record is authoritative.
                    var terminal = await TryReadRunAsync(store, request.Key).ConfigureAwait(false);
                    if (terminal is null)
                        throw;
                    return Result(
                        terminal,
                        terminal.CommittedDeletedCount,
                        terminal.CommittedBatchCount,
                        terminal.State,
                        AuditRetentionCommitKnowledge.Committed,
                        remainingMayExist: terminal.State != AuditRetentionRunState.Completed,
                        terminal.StopReason,
                        AggregateMirror());
                }
                catch (AuditRetentionException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Audit retention batch {BatchOrdinal} of run {RequestId} ended in an unknown commit state.",
                        ordinal,
                        request.RequestId);

                    // A single stale read is not rollback evidence. First look for the exact
                    // receipt; when absent, finalization takes the scope/run lock and its
                    // persisted result is the authoritative commit evidence.
                    var observed = await TryReadRunAsync(store, request.Key).ConfigureAwait(false);
                    var observedReceipt = await TryReadBatchAsync(store, request.Key, ordinal).ConfigureAwait(false);
                    if (observedReceipt is not null)
                    {
                        await TrackMirrorAsync(observed ?? run, observedReceipt).ConfigureAwait(false);
                        var latest = await TryReadRunAsync(store, request.Key).ConfigureAwait(false);
                        if (latest is null || latest.CommittedBatchCount < ordinal)
                        {
                            // The exact receipt proves the batch committed; only the run row is
                            // stale, so the commit fact must stay Committed while the terminal
                            // state and any later batches remain unknown.
                            return Result(
                                run,
                                lastDeleted + observedReceipt.DeletedCount,
                                Math.Max(lastBatches, ordinal),
                                AuditRetentionRunState.Unknown,
                                AuditRetentionCommitKnowledge.Committed,
                                remainingMayExist: true,
                                AuditRetentionStopReasons.UnknownCommit,
                                AggregateMirror());
                        }

                        if (latest.State is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
                        {
                            return Result(
                                latest,
                                latest.CommittedDeletedCount,
                                latest.CommittedBatchCount,
                                latest.State,
                                AuditRetentionCommitKnowledge.Committed,
                                remainingMayExist: latest.State != AuditRetentionRunState.Completed,
                                latest.StopReason,
                                AggregateMirror());
                        }

                        lastDeleted = latest.CommittedDeletedCount;
                        lastBatches = latest.CommittedBatchCount;
                        continue;
                    }

                    var finalized = await TryFinalizeAsync(
                        store,
                        request.Key,
                        AuditRetentionRunState.Failed,
                        AuditRetentionStopReasons.Failed).ConfigureAwait(false);
                    if (finalized is null)
                    {
                        return Result(
                            run,
                            lastDeleted,
                            lastBatches,
                            AuditRetentionRunState.Unknown,
                            AuditRetentionCommitKnowledge.Unknown,
                            remainingMayExist: true,
                            AuditRetentionStopReasons.UnknownCommit,
                            AggregateMirror());
                    }

                    if (finalized.CommittedBatchCount >= ordinal)
                    {
                        // The commit became visible to the locked finalization after the receipt
                        // read raced. Mirror the exact receipt when it is now readable; never
                        // synthesize the batch fact from run totals.
                        var settledReceipt = await TryReadBatchAsync(store, request.Key, ordinal).ConfigureAwait(false);
                        if (settledReceipt is null)
                        {
                            // Finalization confirmed the commit under the scope/run lock; only the
                            // receipt needed to rebuild the mirror is unreadable. Keep the committed
                            // fact and report the mirror gap so an explicit export can retry later.
                            TrackMirrorFailure(AuditRetentionMirrorOutcome.Failed("AUDIT_RETENTION_RECEIPT_UNAVAILABLE"));
                            return Result(
                                finalized,
                                finalized.CommittedDeletedCount,
                                finalized.CommittedBatchCount,
                                finalized.State,
                                AuditRetentionCommitKnowledge.Committed,
                                remainingMayExist: finalized.State != AuditRetentionRunState.Completed,
                                finalized.StopReason,
                                AggregateMirror());
                        }

                        await TrackMirrorAsync(finalized, settledReceipt).ConfigureAwait(false);
                        return Result(
                            finalized,
                            finalized.CommittedDeletedCount,
                            finalized.CommittedBatchCount,
                            finalized.State,
                            AuditRetentionCommitKnowledge.Committed,
                            remainingMayExist: finalized.State != AuditRetentionRunState.Completed,
                            finalized.StopReason,
                            AggregateMirror());
                    }

                    return Result(
                        finalized,
                        finalized.CommittedDeletedCount,
                        finalized.CommittedBatchCount,
                        finalized.State,
                        AuditRetentionCommitKnowledge.None,
                        remainingMayExist: true,
                        finalized.StopReason ?? AuditRetentionStopReasons.Failed,
                        AggregateMirror());
                }

                lastDeleted = batch.CommittedDeletedCount;
                lastBatches = batch.CommittedBatchCount;
                var updatedRun = run with
                {
                    CommittedDeletedCount = batch.CommittedDeletedCount,
                    CommittedBatchCount = batch.CommittedBatchCount,
                    State = batch.RunState,
                    StopReason = batch.StopReason,
                    UpdatedAt = batch.CommittedAt
                };
                var receipt = new AuditRetentionBatchReceipt
                {
                    BatchOrdinal = ordinal,
                    DeletedCount = batch.DeletedCount,
                    CommittedAt = batch.CommittedAt
                };
                await TrackMirrorAsync(updatedRun, receipt).ConfigureAwait(false);

                if (batch.RunState is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
                {
                    return Result(
                        updatedRun,
                        lastDeleted,
                        lastBatches,
                        batch.RunState,
                        AuditRetentionCommitKnowledge.Committed,
                        batch.RemainingMayExist,
                        batch.StopReason,
                        AggregateMirror());
                }

                if (!batch.RemainingMayExist)
                {
                    return Result(
                        updatedRun,
                        lastDeleted,
                        lastBatches,
                        AuditRetentionRunState.Completed,
                        AuditRetentionCommitKnowledge.Committed,
                        remainingMayExist: false,
                        AuditRetentionStopReasons.NoCandidates,
                        AggregateMirror());
                }
            }
        }
        catch (OperationCanceledException)
        {
            var recovered = await TryReadRunAsync(store, request.Key).ConfigureAwait(false);
            if (recovered is null)
            {
                return Result(
                    run,
                    lastDeleted,
                    lastBatches,
                    AuditRetentionRunState.Unknown,
                    AuditRetentionCommitKnowledge.Unknown,
                    remainingMayExist: true,
                    AuditRetentionStopReasons.Cancelled,
                    AggregateMirror());
            }

            if (recovered.State is AuditRetentionRunState.Running or AuditRetentionRunState.Unknown)
            {
                var finalized = await TryFinalizeAsync(
                    store,
                    request.Key,
                    AuditRetentionRunState.Cancelled,
                    AuditRetentionStopReasons.Cancelled).ConfigureAwait(false);
                if (finalized is null)
                {
                    return Result(
                        recovered,
                        recovered.CommittedDeletedCount,
                        recovered.CommittedBatchCount,
                        AuditRetentionRunState.Unknown,
                        AuditRetentionCommitKnowledge.Unknown,
                        remainingMayExist: true,
                        AuditRetentionStopReasons.Cancelled,
                        AggregateMirror());
                }

                return Result(
                    finalized,
                    finalized.CommittedDeletedCount,
                    finalized.CommittedBatchCount,
                    finalized.State,
                    AuditRetentionCommitKnowledge.Committed,
                    remainingMayExist: finalized.State != AuditRetentionRunState.Completed,
                    finalized.StopReason,
                    AggregateMirror());
            }

            // A committed receipt already produced a terminal state; cancellation must not
            // downgrade Completed (or another terminal fact) to Cancelled.
            return Result(
                recovered,
                recovered.CommittedDeletedCount,
                recovered.CommittedBatchCount,
                recovered.State,
                AuditRetentionCommitKnowledge.Committed,
                remainingMayExist: recovered.State != AuditRetentionRunState.Completed,
                recovered.StopReason,
                AggregateMirror());
        }
    }

    public async ValueTask<AuditRetentionRunRecord?> GetRunAsync(
        AuditRetentionRunKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.EnsureValid();
        var store = ResolveStore(key.StoreId, key.SinkId);
        return await store.ReadRunAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AuditRetentionMirrorOutcome> ExportBatchAsync(
        AuditRetentionRunKey key,
        long batchOrdinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.EnsureValid();
        if (batchOrdinal <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchOrdinal), "BatchOrdinal must be positive.");
        var store = ResolveStore(key.StoreId, key.SinkId);
        var run = await store.ReadRunAsync(key, cancellationToken).ConfigureAwait(false)
            ?? throw new AuditRetentionException(
                AuditRetentionErrorCodes.RunNotFound,
                $"Audit retention run '{key.RequestId}' does not exist.");
        var receipt = await store.ReadBatchAsync(key, batchOrdinal, cancellationToken).ConfigureAwait(false)
            ?? throw new AuditRetentionException(
                AuditRetentionErrorCodes.BatchNotFound,
                $"Audit retention run '{key.RequestId}' has no committed batch {batchOrdinal}.");
        return await MirrorBatchAsync(run, receipt).ConfigureAwait(false)
            ?? AuditRetentionMirrorOutcome.Skipped;
    }

    public async ValueTask<AuditHoldResult> PlaceHoldAsync(
        AuditHoldPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        placement.EnsureValid();
        var store = ResolveStore(placement.StoreId, placement.SinkId);
        var hold = await store.PlaceHoldAsync(placement, cancellationToken).ConfigureAwait(false);
        var mirror = await MirrorHoldAsync(hold, release: false).ConfigureAwait(false);
        return new AuditHoldResult { Hold = hold, MirrorOutcome = mirror };
    }

    public async ValueTask<AuditHoldResult> ReleaseHoldAsync(
        AuditHoldRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        release.EnsureValid();
        var store = ResolveStore(release.StoreId, release.SinkId);
        var hold = await store.ReleaseHoldAsync(release, cancellationToken).ConfigureAwait(false);
        var mirror = await MirrorHoldAsync(hold, release: true).ConfigureAwait(false);
        return new AuditHoldResult { Hold = hold, MirrorOutcome = mirror };
    }

    private IAuditRetentionStore ResolveStore(string storeId, string sinkId)
    {
        if (!_stores.TryGetValue(storeId, out var store))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.StoreUnknown,
                $"Audit retention store '{storeId}' is not registered.");
        }

        if (!string.Equals(store.Identity.SinkId, sinkId, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.StoreMismatch,
                $"Audit retention store '{storeId}' owns sink '{store.Identity.SinkId}', not '{sinkId}'.");
        }

        if (!store.Identity.Capabilities.IsComplete)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.CapabilityMissing,
                $"Audit retention store '{storeId}' does not declare the full retention capability set.");
        }

        return store;
    }

    private static async ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
        IAuditRetentionStore store,
        AuditRetentionRunRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.CreateOrReadRunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditRetentionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            using var reconciliation = new CancellationTokenSource(ReceiptReconciliationTimeout);
            var recovered = await store.ReadRunAsync(request.Key, reconciliation.Token).ConfigureAwait(false);
            if (recovered is null)
                throw;
            return recovered;
        }
    }

    private async ValueTask<AuditRetentionRunRecord?> TryReadRunAsync(
        IAuditRetentionStore store,
        AuditRetentionRunKey key)
    {
        try
        {
            using var reconciliation = new CancellationTokenSource(ReceiptReconciliationTimeout);
            return await store.ReadRunAsync(key, reconciliation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Audit retention receipt reconciliation failed for run {RequestId}.", key.RequestId);
            return null;
        }
    }

    private async ValueTask<AuditRetentionBatchReceipt?> TryReadBatchAsync(
        IAuditRetentionStore store,
        AuditRetentionRunKey key,
        long batchOrdinal)
    {
        try
        {
            using var reconciliation = new CancellationTokenSource(ReceiptReconciliationTimeout);
            return await store.ReadBatchAsync(key, batchOrdinal, reconciliation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Audit retention receipt reconciliation failed for run {RequestId} batch {BatchOrdinal}.",
                key.RequestId,
                batchOrdinal);
            return null;
        }
    }

    private async ValueTask<AuditRetentionRunRecord?> TryFinalizeAsync(
        IAuditRetentionStore store,
        AuditRetentionRunKey key,
        AuditRetentionRunState state,
        string stopReason)
    {
        try
        {
            using var reconciliation = new CancellationTokenSource(ReceiptReconciliationTimeout);
            return await store.FinalizeRunAsync(
                new AuditRetentionRunFinalization { Key = key, State = state, StopReason = stopReason },
                reconciliation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Audit retention run {RequestId} terminal state '{State}' could not be persisted; committed batch receipts remain authoritative.",
                key.RequestId,
                state);
            return null;
        }
    }

    private async ValueTask<AuditRetentionMirrorOutcome?> MirrorBatchAsync(
        AuditRetentionRunRecord run,
        AuditRetentionBatchReceipt receipt)
    {
        if (_recorder is null)
            return null;
        try
        {
            var result = await _recorder.RecordAsync(
                AuditRetentionFactFactory.Batch(run, receipt),
                CancellationToken.None).ConfigureAwait(false);
            return result.IsAccepted
                ? AuditRetentionMirrorOutcome.Succeeded
                : AuditRetentionMirrorOutcome.Failed(result.Issues.FirstOrDefault()?.Code ?? result.Status.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Audit retention batch mirror failed for run {RequestId}.", run.Key.RequestId);
            return AuditRetentionMirrorOutcome.Failed("AUDIT_RETENTION_MIRROR_FAILURE");
        }
    }

    private async ValueTask<AuditRetentionMirrorOutcome> MirrorHoldAsync(AuditHold hold, bool release)
    {
        if (_recorder is null)
            return AuditRetentionMirrorOutcome.Skipped;
        try
        {
            var result = await _recorder.RecordAsync(
                AuditRetentionFactFactory.Hold(hold, release),
                CancellationToken.None).ConfigureAwait(false);
            return result.IsAccepted
                ? AuditRetentionMirrorOutcome.Succeeded
                : AuditRetentionMirrorOutcome.Failed(result.Issues.FirstOrDefault()?.Code ?? result.Status.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Audit retention hold mirror failed for hold {HoldId}.", hold.HoldId);
            return AuditRetentionMirrorOutcome.Failed("AUDIT_RETENTION_MIRROR_FAILURE");
        }
    }

    private static AuditRetentionRunResult StoredResult(AuditRetentionRunRecord run)
        => Result(
            run,
            run.CommittedDeletedCount,
            run.CommittedBatchCount,
            run.State,
            AuditRetentionCommitKnowledge.Committed,
            remainingMayExist: run.State is AuditRetentionRunState.BudgetReached
                or AuditRetentionRunState.Cancelled
                or AuditRetentionRunState.Failed
                or AuditRetentionRunState.Unknown,
            run.StopReason,
            mirror: null);

    private static AuditRetentionRunResult Result(
        AuditRetentionRunRecord run,
        long deleted,
        long batches,
        AuditRetentionRunState state,
        AuditRetentionCommitKnowledge knowledge,
        bool remainingMayExist,
        string? stopReason,
        AuditRetentionMirrorOutcome? mirror)
        => new()
        {
            Key = run.Key,
            State = state,
            CommitKnowledge = knowledge,
            CommittedDeletedCount = deleted,
            CommittedBatchCount = batches,
            RemainingMayExist = remainingMayExist,
            StopReason = stopReason,
            MirrorOutcome = mirror
        };
}
