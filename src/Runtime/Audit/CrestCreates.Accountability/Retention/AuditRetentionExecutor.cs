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

        if (run.State is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
        {
            return StoredResult(run);
        }

        var lastDeleted = run.CommittedDeletedCount;
        var lastBatches = run.CommittedBatchCount;
        var startedAt = _timeProvider.GetUtcNow();

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
                    return Result(
                        finalized ?? run,
                        lastDeleted,
                        lastBatches,
                        AuditRetentionRunState.BudgetReached,
                        AuditRetentionCommitKnowledge.Committed,
                        remainingMayExist: true,
                        AuditRetentionStopReasons.BudgetReached,
                        mirror: null);
                }

                var ordinal = lastBatches + 1;
                AuditRetentionBatchOutcome batch;
                try
                {
                    batch = await store.ExecuteBatchAsync(
                        new AuditRetentionBatchRequest { Key = request.Key, BatchOrdinal = ordinal },
                        cancellationToken).ConfigureAwait(false);
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
                            AuditRetentionStopReasons.UnknownCommit,
                            mirror: null);
                    }

                    if (recovered.CommittedBatchCount >= ordinal)
                    {
                        var deleted = ClampDeleted(recovered.CommittedDeletedCount - lastDeleted);
                        var recoveredBatch = new AuditRetentionBatchOutcome
                        {
                            BatchOrdinal = ordinal,
                            DeletedCount = deleted,
                            CommittedDeletedCount = recovered.CommittedDeletedCount,
                            CommittedBatchCount = recovered.CommittedBatchCount,
                            RemainingMayExist = recovered.State != AuditRetentionRunState.Completed,
                            RunState = recovered.State,
                            StopReason = recovered.StopReason,
                            FromExistingReceipt = true,
                            CommittedAt = recovered.UpdatedAt
                        };
                        var recoveredMirror = await MirrorBatchAsync(recovered, recoveredBatch).ConfigureAwait(false);
                        if (recovered.State is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
                        {
                            return Result(
                                recovered,
                                recovered.CommittedDeletedCount,
                                recovered.CommittedBatchCount,
                                recovered.State,
                                AuditRetentionCommitKnowledge.Committed,
                                remainingMayExist: recovered.State != AuditRetentionRunState.Completed,
                                recovered.StopReason,
                                recoveredMirror);
                        }

                        lastDeleted = recovered.CommittedDeletedCount;
                        lastBatches = recovered.CommittedBatchCount;
                        continue;
                    }

                    var failed = await TryFinalizeAsync(
                        store,
                        request.Key,
                        AuditRetentionRunState.Failed,
                        AuditRetentionStopReasons.Failed).ConfigureAwait(false);
                    return Result(
                        failed ?? run,
                        lastDeleted,
                        lastBatches,
                        AuditRetentionRunState.Failed,
                        AuditRetentionCommitKnowledge.None,
                        remainingMayExist: true,
                        AuditRetentionStopReasons.Failed,
                        mirror: null);
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
                var mirror = await MirrorBatchAsync(updatedRun, batch).ConfigureAwait(false);

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
                        mirror);
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
                        mirror);
                }
            }
        }
        catch (OperationCanceledException)
        {
            var recovered = await TryReadRunAsync(store, request.Key).ConfigureAwait(false);
            if (recovered is not null)
            {
                if (recovered.State is AuditRetentionRunState.Running or AuditRetentionRunState.Unknown)
                {
                    recovered = await TryFinalizeAsync(
                        store,
                        request.Key,
                        AuditRetentionRunState.Cancelled,
                        AuditRetentionStopReasons.Cancelled).ConfigureAwait(false) ?? recovered;
                }

                return Result(
                    recovered,
                    recovered.CommittedDeletedCount,
                    recovered.CommittedBatchCount,
                    AuditRetentionRunState.Cancelled,
                    AuditRetentionCommitKnowledge.Committed,
                    remainingMayExist: true,
                    AuditRetentionStopReasons.Cancelled,
                    mirror: null);
            }

            return Result(
                run,
                lastDeleted,
                lastBatches,
                AuditRetentionRunState.Cancelled,
                AuditRetentionCommitKnowledge.Unknown,
                remainingMayExist: true,
                AuditRetentionStopReasons.Cancelled,
                mirror: null);
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

    private async ValueTask<AuditRetentionMirrorOutcome> MirrorBatchAsync(
        AuditRetentionRunRecord run,
        AuditRetentionBatchOutcome batch)
    {
        if (_recorder is null)
            return AuditRetentionMirrorOutcome.Skipped;
        try
        {
            var result = await _recorder.RecordAsync(
                AuditRetentionFactFactory.Batch(run, batch),
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

    private static int ClampDeleted(long value)
        => value < 0 ? 0 : value > int.MaxValue ? int.MaxValue : (int)value;
}
