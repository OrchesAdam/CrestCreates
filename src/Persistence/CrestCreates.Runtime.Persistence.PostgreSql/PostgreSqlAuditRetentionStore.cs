using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// PostgreSQL retention participant. Every batch is one local transaction that serializes on the
/// (sink, scope) lock row, locks the run, deletes bodies with RETURNING, transitions the ledger,
/// and writes the receipt together. The persisted run is the only budget authority.
/// </summary>
public sealed class PostgreSqlAuditRetentionStore : IAuditRetentionStore
{
    public const string StoreIdValue = "postgresql-audit-retention";
    public const string SinkIdValue = "postgresql-runtime-audit";

    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly string _ledger;
    private readonly string _envelopes;
    private readonly string _scopeLocks;
    private readonly string _runs;
    private readonly string _batches;
    private readonly string _holds;
    private readonly string _holdReceipts;

    public PostgreSqlAuditRetentionStore(
        NpgsqlDataSource dataSource,
        PostgreSqlRuntimePersistenceOptions options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledger = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_ledger");
        _envelopes = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_envelopes");
        _scopeLocks = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_scope_locks");
        _runs = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_runs");
        _batches = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_batches");
        _holds = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_holds");
        _holdReceipts = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_audit_retention_hold_receipts");
    }

    public AuditRetentionStoreIdentity Identity { get; } = new()
    {
        StoreId = StoreIdValue,
        SinkId = SinkIdValue,
        Capabilities = AuditRetentionStoreCapabilities.Full
    };

    public async ValueTask<AuditRetentionRunRecord> CreateOrReadRunAsync(
        AuditRetentionRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        EnsureStore(request.StoreId, request.SinkId);
        var runId = AuditRetentionSemanticNames.RunReceiptId(request.Key);
        var policyHash = request.Policy.ComputeHash();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockScopeAsync(connection, transaction, request.Scope, cancellationToken).ConfigureAwait(false);

        var existing = await ReadRunByRequestAsync(connection, transaction, request.Key, forUpdate: true, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureSameBinding(existing, policyHash, request.ReasonCode);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                 insert into {_runs} (
                     run_id, store_id, sink_id, scope_kind, tenant_id, request_id, reason_code,
                     actor_kind, actor_id, actor_display_name, correlation_id,
                     retention_days, batch_size, max_batches, max_run_seconds, policy_hash,
                     snapshot_at, cutoff, state, committed_deleted_count, committed_batch_count, stop_reason)
                 values (
                     @run, @store, @sink, @scope, @tenant, @request, @reason,
                     @actor_kind, @actor_id, @actor_name, @correlation,
                     @days, @batch, @batches, @seconds, @policy_hash,
                     @snapshot, @cutoff, @state, 0, 0, null)
                 returning created_at, updated_at;
                 """,
                connection,
                transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds
            };
            AddRunParameters(command, request, runId, policyHash);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit retention run insert did not return timestamps.");
            var createdAt = reader.GetFieldValue<DateTimeOffset>(0);
            var updatedAt = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.DisposeAsync().ConfigureAwait(false);

            var record = ToRecord(request, runId, policyHash, AuditRetentionRunState.Running, 0, 0, null, createdAt, updatedAt);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return record;
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, "uq_runtime_audit_retention_runs_request"))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            var concurrent = await ReadRunByRequestAsync(connection, null, request.Key, forUpdate: false, cancellationToken).ConfigureAwait(false)
                ?? throw new AuditRetentionException(
                    AuditRetentionErrorCodes.RequestConflict,
                    "A concurrent audit retention run could not be read after a uniqueness conflict.");
            EnsureSameBinding(concurrent, policyHash, request.ReasonCode);
            return concurrent;
        }
    }

    public async ValueTask<AuditRetentionPreview> PreviewAsync(
        AuditRetentionPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        EnsureStore(request.StoreId, request.SinkId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await using (var readOnly = new NpgsqlCommand("set transaction read only;", connection, transaction))
        {
            await readOnly.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var sample = new List<AuditRetentionPreviewCandidate>();
        var limit = request.Policy.BatchSize + 1;
        await using (var command = new NpgsqlCommand(
            $"""
             select l.audit_id, l.occurred_at, l.first_accepted_at,
                    case
                        when l.body_state = 'Quarantined' then 4
                        when l.protection = 'governance' then 3
                        when exists (
                            select 1 from {_holds} h
                            where h.sink_id = l.sink_id and h.audit_id = l.audit_id and h.released_at is null) then 2
                        else 1
                    end as disposition
             from {_ledger} l
             where l.sink_id = @sink
               and ((l.scope_kind = @scope and l.tenant_id = @tenant)
                    or (@scope = 'host' and l.scope_kind = 'quarantine'))
               and l.first_accepted_at < @cutoff
               and l.occurred_at < @cutoff
               and (l.body_state = 'BodyPresent' or l.body_state = 'Quarantined')
             order by l.first_accepted_at, l.audit_id collate "C"
             limit @limit;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        })
        {
            command.Parameters.AddWithValue("sink", SinkIdValue);
            AddScopeParameters(command, request.Scope);
            command.Parameters.AddWithValue("cutoff", request.Cutoff);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                sample.Add(new AuditRetentionPreviewCandidate
                {
                    AuditId = reader.GetString(0),
                    OccurredAt = reader.GetFieldValue<DateTimeOffset>(1),
                    FirstAcceptedAt = reader.GetFieldValue<DateTimeOffset>(2),
                    Disposition = (AuditRetentionCandidateDisposition)reader.GetInt32(3)
                });
            }
        }

        var hasMore = sample.Count > request.Policy.BatchSize;
        if (hasMore)
            sample.RemoveAt(sample.Count - 1);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AuditRetentionPreview
        {
            StoreId = StoreIdValue,
            SinkId = SinkIdValue,
            Scope = request.Scope,
            SnapshotAt = request.SnapshotAt,
            Cutoff = request.Cutoff,
            PolicyHash = request.Policy.ComputeHash(),
            BatchSize = request.Policy.BatchSize,
            MaxBatchesPerRun = request.Policy.MaxBatchesPerRun,
            HasMore = hasMore,
            Sample = sample,
            EligibleInSample = sample.Count(candidate => candidate.Disposition == AuditRetentionCandidateDisposition.Eligible),
            HeldInSample = sample.Count(candidate => candidate.Disposition == AuditRetentionCandidateDisposition.ActiveHold),
            ProtectedInSample = sample.Count(candidate => candidate.Disposition == AuditRetentionCandidateDisposition.Protected),
            QuarantinedInSample = sample.Count(candidate => candidate.Disposition == AuditRetentionCandidateDisposition.Quarantined)
        };
    }

    public async ValueTask<AuditRetentionBatchOutcome> ExecuteBatchAsync(
        AuditRetentionBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        EnsureStore(request.Key.StoreId, request.Key.SinkId);
        var runId = AuditRetentionSemanticNames.RunReceiptId(request.Key);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockScopeAsync(connection, transaction, request.Key.Scope, cancellationToken).ConfigureAwait(false);

        var run = await ReadRunByIdAsync(connection, transaction, runId, forUpdate: true, cancellationToken).ConfigureAwait(false)
            ?? throw new AuditRetentionException(AuditRetentionErrorCodes.RunNotFound, $"Audit retention run '{request.Key.RequestId}' does not exist.");
        EnsureRunMatchesKey(run, request.Key);

        var existingReceipt = await ReadBatchReceiptAsync(connection, transaction, runId, request.BatchOrdinal, cancellationToken).ConfigureAwait(false);
        if (existingReceipt is not null)
        {
            var remaining = await RemainingMayExistAsync(connection, transaction, run, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AuditRetentionBatchOutcome
            {
                BatchOrdinal = request.BatchOrdinal,
                DeletedCount = existingReceipt.Value.DeletedCount,
                CommittedDeletedCount = run.CommittedDeletedCount,
                CommittedBatchCount = run.CommittedBatchCount,
                RemainingMayExist = remaining,
                RunState = run.State,
                StopReason = run.StopReason,
                FromExistingReceipt = true,
                CommittedAt = existingReceipt.Value.CommittedAt
            };
        }

        if (request.BatchOrdinal != run.CommittedBatchCount + 1)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.BatchOrdinalConflict,
                $"Audit retention batch ordinal {request.BatchOrdinal} does not follow committed batch {run.CommittedBatchCount}.");
        }

        if (run.CommittedBatchCount >= run.Policy.MaxBatchesPerRun)
        {
            await UpdateRunAsync(
                connection,
                transaction,
                runId,
                run.CommittedDeletedCount,
                run.CommittedBatchCount,
                AuditRetentionRunState.BudgetReached,
                AuditRetentionStopReasons.BudgetReached,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AuditRetentionBatchOutcome
            {
                BatchOrdinal = request.BatchOrdinal,
                DeletedCount = 0,
                CommittedDeletedCount = run.CommittedDeletedCount,
                CommittedBatchCount = run.CommittedBatchCount,
                RemainingMayExist = true,
                RunState = AuditRetentionRunState.BudgetReached,
                StopReason = AuditRetentionStopReasons.BudgetReached,
                FromExistingReceipt = false,
                CommittedAt = run.UpdatedAt
            };
        }

        var candidates = await SelectCandidatesAsync(connection, transaction, run, cancellationToken).ConfigureAwait(false);
        var deletedCount = 0;
        if (candidates.Count > 0)
        {
            var deleted = await DeleteBodiesAsync(connection, transaction, candidates, cancellationToken).ConfigureAwait(false);
            if (deleted != candidates.Count)
            {
                throw new AuditRetentionException(
                    AuditRetentionErrorCodes.LedgerInvariantViolation,
                    $"Audit retention batch deleted {deleted} bodies but selected {candidates.Count} identities.");
            }

            deletedCount = deleted;
            var transitioned = await MarkPurgedAsync(connection, transaction, candidates, cancellationToken).ConfigureAwait(false);
            if (transitioned != deleted)
            {
                throw new AuditRetentionException(
                    AuditRetentionErrorCodes.LedgerInvariantViolation,
                    $"Audit retention batch transitioned {transitioned} ledger rows but deleted {deleted} bodies.");
            }
        }

        var remainingMayExist = await RemainingMayExistAsync(connection, transaction, run, cancellationToken).ConfigureAwait(false);
        var nextBatchCount = run.CommittedBatchCount + 1;
        var state = !remainingMayExist
            ? AuditRetentionRunState.Completed
            : nextBatchCount >= run.Policy.MaxBatchesPerRun
                ? AuditRetentionRunState.BudgetReached
                : AuditRetentionRunState.Running;
        var stopReason = state switch
        {
            AuditRetentionRunState.Completed => AuditRetentionStopReasons.NoCandidates,
            AuditRetentionRunState.BudgetReached => AuditRetentionStopReasons.BudgetReached,
            _ => null
        };
        var totalDeleted = run.CommittedDeletedCount + deletedCount;
        var committedAt = await InsertBatchReceiptAsync(
            connection,
            transaction,
            run,
            request.BatchOrdinal,
            deletedCount,
            candidates,
            cancellationToken).ConfigureAwait(false);
        await UpdateRunAsync(
            connection,
            transaction,
            runId,
            totalDeleted,
            nextBatchCount,
            state,
            stopReason,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new AuditRetentionBatchOutcome
        {
            BatchOrdinal = request.BatchOrdinal,
            DeletedCount = deletedCount,
            CommittedDeletedCount = totalDeleted,
            CommittedBatchCount = nextBatchCount,
            RemainingMayExist = remainingMayExist,
            RunState = state,
            StopReason = stopReason,
            FromExistingReceipt = false,
            CommittedAt = committedAt
        };
    }

    public async ValueTask<AuditRetentionRunRecord?> ReadRunAsync(
        AuditRetentionRunKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.EnsureValid();
        EnsureStore(key.StoreId, key.SinkId);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadRunByRequestAsync(connection, null, key, forUpdate: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AuditRetentionRunRecord> FinalizeRunAsync(
        AuditRetentionRunFinalization finalization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalization);
        finalization.EnsureValid();
        EnsureStore(finalization.Key.StoreId, finalization.Key.SinkId);
        var runId = AuditRetentionSemanticNames.RunReceiptId(finalization.Key);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockScopeAsync(connection, transaction, finalization.Key.Scope, cancellationToken).ConfigureAwait(false);
        var run = await ReadRunByIdAsync(connection, transaction, runId, forUpdate: true, cancellationToken).ConfigureAwait(false)
            ?? throw new AuditRetentionException(AuditRetentionErrorCodes.RunNotFound, $"Audit retention run '{finalization.Key.RequestId}' does not exist.");
        EnsureRunMatchesKey(run, finalization.Key);
        if (run.State is not (AuditRetentionRunState.Running or AuditRetentionRunState.Unknown))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return run;
        }

        var updated = await UpdateRunAsync(
            connection,
            transaction,
            runId,
            run.CommittedDeletedCount,
            run.CommittedBatchCount,
            finalization.State,
            finalization.StopReason,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return run with
        {
            State = finalization.State,
            StopReason = finalization.StopReason,
            UpdatedAt = updated
        };
    }

    public async ValueTask<AuditHold> PlaceHoldAsync(
        AuditHoldPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        placement.EnsureValid();
        EnsureStore(placement.StoreId, placement.SinkId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockScopeAsync(connection, transaction, placement.Scope, cancellationToken).ConfigureAwait(false);

        var bodyState = await ReadLedgerBodyStateAsync(connection, transaction, placement.Scope, placement.AuditId, cancellationToken).ConfigureAwait(false);
        if (bodyState is null)
            throw new AuditRetentionException(AuditRetentionErrorCodes.HoldNotFound, $"Audit record '{placement.AuditId}' does not exist in the requested retention scope.");
        if (string.Equals(bodyState, "Purged", StringComparison.Ordinal))
            throw new AuditRetentionException(AuditRetentionErrorCodes.HoldAlreadyPurged, $"Audit record '{placement.AuditId}' has already been purged.");
        if (!string.Equals(bodyState, "BodyPresent", StringComparison.Ordinal))
            throw new AuditRetentionException(AuditRetentionErrorCodes.HoldNotFound, $"Audit record '{placement.AuditId}' has no active body.");

        var holdId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand(
            $"""
             insert into {_holds} (
                 hold_id, sink_id, audit_id, scope_kind, tenant_id, reason_code,
                 actor_kind, actor_id, actor_display_name, correlation_id, version)
             values (@hold, @sink, @audit, @scope, @tenant, @reason, @actor_kind, @actor_id, @actor_name, @correlation, 1)
             returning created_at;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        })
        {
            command.Parameters.AddWithValue("hold", holdId);
            command.Parameters.AddWithValue("sink", SinkIdValue);
            command.Parameters.AddWithValue("audit", placement.AuditId);
            AddScopeParameters(command, placement.Scope);
            command.Parameters.AddWithValue("reason", placement.ReasonCode);
            AddActorParameters(command, placement.Actor);
            command.Parameters.AddWithValue("correlation", placement.CorrelationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit hold insert did not return a timestamp.");
            var createdAt = reader.GetFieldValue<DateTimeOffset>(0);
            await reader.DisposeAsync().ConfigureAwait(false);

            await InsertHoldReceiptAsync(
                connection,
                transaction,
                holdId,
                1,
                "place",
                placement.Scope,
                placement.AuditId,
                placement.ReasonCode,
                placement.Actor,
                placement.CorrelationId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new AuditHold
            {
                HoldId = holdId,
                StoreId = StoreIdValue,
                SinkId = SinkIdValue,
                AuditId = placement.AuditId,
                Scope = placement.Scope,
                Version = 1,
                ReasonCode = placement.ReasonCode,
                Actor = placement.Actor,
                CorrelationId = placement.CorrelationId,
                CreatedAt = createdAt
            };
        }
    }

    public async ValueTask<AuditHold> ReleaseHoldAsync(
        AuditHoldRelease release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        release.EnsureValid();
        EnsureStore(release.StoreId, release.SinkId);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await LockScopeAsync(connection, transaction, release.Scope, cancellationToken).ConfigureAwait(false);

        var hold = await ReadHoldForUpdateAsync(connection, transaction, release.HoldId, cancellationToken).ConfigureAwait(false);
        if (hold is null
            || !string.Equals(hold.Scope.StableSignature(), release.Scope.StableSignature(), StringComparison.Ordinal))
        {
            throw new AuditRetentionException(AuditRetentionErrorCodes.HoldNotFound, $"Audit hold '{release.HoldId}' does not exist in the requested retention scope.");
        }

        if (hold.Version != release.ExpectedVersion || hold.ReleasedAt is not null)
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.HoldVersionConflict,
                $"Audit hold '{release.HoldId}' version {release.ExpectedVersion} is not the active version.");
        }

        var newVersion = hold.Version + 1;
        DateTimeOffset releasedAt;
        await using (var command = new NpgsqlCommand(
            $"""
             update {_holds}
             set version = @version, released_at = clock_timestamp(),
                 release_reason_code = @reason, release_actor_kind = @actor_kind,
                 release_actor_id = @actor_id, release_correlation_id = @correlation
             where hold_id = @hold and version = @expected and released_at is null
             returning released_at;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        })
        {
            command.Parameters.AddWithValue("version", newVersion);
            command.Parameters.AddWithValue("reason", release.ReasonCode);
            command.Parameters.AddWithValue("actor_kind", release.Actor.Kind);
            command.Parameters.AddWithValue("actor_id", release.Actor.Id);
            command.Parameters.AddWithValue("correlation", release.CorrelationId);
            command.Parameters.AddWithValue("hold", release.HoldId);
            command.Parameters.AddWithValue("expected", release.ExpectedVersion);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
                throw new AuditRetentionException(AuditRetentionErrorCodes.HoldVersionConflict, $"Audit hold '{release.HoldId}' was released concurrently.");
            releasedAt = value switch
            {
                DateTimeOffset timestamp => timestamp,
                DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                _ => throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit hold release did not return a timestamp.")
            };
        }

        await InsertHoldReceiptAsync(
            connection,
            transaction,
            release.HoldId,
            newVersion,
            "release",
            release.Scope,
            hold.AuditId,
            release.ReasonCode,
            release.Actor,
            release.CorrelationId,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return hold with
        {
            Version = newVersion,
            ReleasedAt = releasedAt,
            ReleaseActor = release.Actor,
            ReleaseCorrelationId = release.CorrelationId,
            ReleaseReasonCode = release.ReasonCode
        };
    }

    private void EnsureStore(string storeId, string sinkId)
    {
        if (!string.Equals(storeId, StoreIdValue, StringComparison.Ordinal)
            || !string.Equals(sinkId, SinkIdValue, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.StoreMismatch,
                $"PostgreSQL audit retention store serves '{StoreIdValue}' and sink '{SinkIdValue}'.");
        }
    }

    private static void EnsureSameBinding(AuditRetentionRunRecord run, string policyHash, string reasonCode)
    {
        if (!string.Equals(run.PolicyHash, policyHash, StringComparison.Ordinal)
            || !string.Equals(run.ReasonCode, reasonCode, StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.RequestConflict,
                "The audit retention request id is already bound to a different policy or reason.");
        }
    }

    private static void EnsureRunMatchesKey(AuditRetentionRunRecord run, AuditRetentionRunKey key)
    {
        if (!string.Equals(run.Key.StoreId, key.StoreId, StringComparison.Ordinal)
            || !string.Equals(run.Key.SinkId, key.SinkId, StringComparison.Ordinal)
            || !string.Equals(run.Key.RequestId, key.RequestId, StringComparison.Ordinal)
            || !string.Equals(run.Key.Scope.StableSignature(), key.Scope.StableSignature(), StringComparison.Ordinal))
        {
            throw new AuditRetentionException(
                AuditRetentionErrorCodes.RequestConflict,
                "The persisted audit retention run does not match the requested run key.");
        }
    }

    private void AddRunParameters(NpgsqlCommand command, AuditRetentionRunRequest request, string runId, string policyHash)
    {
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("store", request.StoreId);
        command.Parameters.AddWithValue("sink", request.SinkId);
        AddScopeParameters(command, request.Scope);
        command.Parameters.AddWithValue("request", request.RequestId);
        command.Parameters.AddWithValue("reason", request.ReasonCode);
        AddActorParameters(command, request.Actor);
        command.Parameters.AddWithValue("correlation", request.CorrelationId);
        command.Parameters.AddWithValue("days", request.Policy.RetentionDays);
        command.Parameters.AddWithValue("batch", request.Policy.BatchSize);
        command.Parameters.AddWithValue("batches", request.Policy.MaxBatchesPerRun);
        command.Parameters.AddWithValue("seconds", request.Policy.MaxRunSeconds);
        command.Parameters.AddWithValue("policy_hash", policyHash);
        command.Parameters.AddWithValue("snapshot", request.SnapshotAt);
        command.Parameters.AddWithValue("cutoff", request.Cutoff);
        command.Parameters.AddWithValue("state", (int)AuditRetentionRunState.Running);
    }

    private static void AddActorParameters(NpgsqlCommand command, AuditActor actor)
    {
        command.Parameters.AddWithValue("actor_kind", actor.Kind);
        command.Parameters.AddWithValue("actor_id", actor.Id);
        if (actor.DisplayName is null)
            command.Parameters.Add("actor_name", NpgsqlTypes.NpgsqlDbType.Text).Value = DBNull.Value;
        else
            command.Parameters.AddWithValue("actor_name", actor.DisplayName);
    }

    private static void AddScopeParameters(NpgsqlCommand command, AuditRetentionScope scope)
    {
        command.Parameters.AddWithValue("scope", scope.Kind);
        command.Parameters.AddWithValue("tenant", scope.TenantId ?? string.Empty);
    }

    private async Task LockScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRetentionScope scope,
        CancellationToken cancellationToken)
    {
        scope.EnsureValid();
        await using (var insert = new NpgsqlCommand(
            $"insert into {_scopeLocks} (sink_id, scope_kind, tenant_id) values (@sink, @scope, @tenant) on conflict (sink_id, scope_kind, tenant_id) do nothing;",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        })
        {
            insert.Parameters.AddWithValue("sink", SinkIdValue);
            AddScopeParameters(insert, scope);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand(
            $"select 1 from {_scopeLocks} where sink_id=@sink and scope_kind=@scope and tenant_id=@tenant for update;",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", SinkIdValue);
        AddScopeParameters(command, scope);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuditRetentionRunRecord?> ReadRunByRequestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AuditRetentionRunKey key,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             select run_id, store_id, sink_id, scope_kind, tenant_id, request_id, reason_code,
                    actor_kind, actor_id, actor_display_name, correlation_id,
                    retention_days, batch_size, max_batches, max_run_seconds, policy_hash,
                    snapshot_at, cutoff, state, committed_deleted_count, committed_batch_count, stop_reason,
                    created_at, updated_at
             from {_runs}
             where store_id=@store and sink_id=@sink and scope_kind=@scope and tenant_id=@tenant and request_id=@request
             {(forUpdate ? "for update" : string.Empty)};
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("store", key.StoreId);
        command.Parameters.AddWithValue("sink", key.SinkId);
        AddScopeParameters(command, key.Scope);
        command.Parameters.AddWithValue("request", key.RequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRun(reader) : null;
    }

    private async Task<AuditRetentionRunRecord?> ReadRunByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string runId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             select run_id, store_id, sink_id, scope_kind, tenant_id, request_id, reason_code,
                    actor_kind, actor_id, actor_display_name, correlation_id,
                    retention_days, batch_size, max_batches, max_run_seconds, policy_hash,
                    snapshot_at, cutoff, state, committed_deleted_count, committed_batch_count, stop_reason,
                    created_at, updated_at
             from {_runs}
             where run_id=@run
             {(forUpdate ? "for update" : string.Empty)};
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("run", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRun(reader) : null;
    }

    private static AuditRetentionRunRecord ReadRun(NpgsqlDataReader reader)
        => new()
        {
            Key = new AuditRetentionRunKey
            {
                StoreId = reader.GetString(1),
                SinkId = reader.GetString(2),
                Scope = ReadScope(reader, 3, 4),
                RequestId = reader.GetString(5)
            },
            ReasonCode = reader.GetString(6),
            Actor = new AuditActor
            {
                Kind = reader.GetString(7),
                Id = reader.GetString(8),
                DisplayName = reader.IsDBNull(9) ? null : reader.GetString(9)
            },
            CorrelationId = reader.GetString(10),
            Policy = new AuditRetentionPolicy
            {
                RetentionDays = reader.GetInt32(11),
                BatchSize = reader.GetInt32(12),
                MaxBatchesPerRun = reader.GetInt32(13),
                MaxRunSeconds = reader.GetInt32(14)
            },
            PolicyHash = reader.GetString(15),
            SnapshotAt = reader.GetFieldValue<DateTimeOffset>(16),
            Cutoff = reader.GetFieldValue<DateTimeOffset>(17),
            State = (AuditRetentionRunState)reader.GetInt32(18),
            CommittedDeletedCount = reader.GetInt64(19),
            CommittedBatchCount = reader.GetInt64(20),
            StopReason = reader.IsDBNull(21) ? null : reader.GetString(21),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(22),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(23)
        };

    private static AuditRetentionScope ReadScope(NpgsqlDataReader reader, int kindOrdinal, int tenantOrdinal)
        => string.Equals(reader.GetString(kindOrdinal), AuditRetentionScope.HostKind, StringComparison.Ordinal)
            ? AuditRetentionScope.Host
            : AuditRetentionScope.ForTenant(reader.GetString(tenantOrdinal));

    private async Task<List<SelectedCandidate>> SelectCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRetentionRunRecord run,
        CancellationToken cancellationToken)
    {
        var candidates = new List<SelectedCandidate>();
        await using var command = new NpgsqlCommand(
            $"""
             select l.audit_id, l.integrity_json::text
             from {_ledger} l
             where l.sink_id = @sink and l.scope_kind = @scope and l.tenant_id = @tenant
               and l.body_state = 'BodyPresent'
               and l.protection = 'none'
               and l.first_accepted_at < @cutoff
               and l.occurred_at < @cutoff
               and not exists (
                   select 1 from {_holds} h
                   where h.sink_id = l.sink_id and h.audit_id = l.audit_id and h.released_at is null)
             order by l.first_accepted_at, l.audit_id collate "C"
             limit @limit
             for update of l;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", run.Key.SinkId);
        AddScopeParameters(command, run.Key.Scope);
        command.Parameters.AddWithValue("cutoff", run.Cutoff);
        command.Parameters.AddWithValue("limit", run.Policy.BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            candidates.Add(new SelectedCandidate(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        return candidates;
    }

    private async Task<int> DeleteBodiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<SelectedCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"delete from {_envelopes} where sink_id=@sink and body_state='BodyPresent' and audit_id = any(@ids) returning audit_id;",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", SinkIdValue);
        command.Parameters.AddWithValue("ids", candidates.Select(candidate => candidate.AuditId).ToArray());
        var deleted = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            deleted++;
        return deleted;
    }

    private async Task<int> MarkPurgedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<SelectedCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"update {_ledger} set body_state='Purged', projected_at=clock_timestamp() where sink_id=@sink and body_state='BodyPresent' and audit_id = any(@ids);",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", SinkIdValue);
        command.Parameters.AddWithValue("ids", candidates.Select(candidate => candidate.AuditId).ToArray());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RemainingMayExistAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRetentionRunRecord run,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             select exists (
                 select 1 from {_ledger} l
                 where l.sink_id = @sink and l.scope_kind = @scope and l.tenant_id = @tenant
                   and l.body_state = 'BodyPresent'
                   and l.protection = 'none'
                   and l.first_accepted_at < @cutoff
                   and l.occurred_at < @cutoff
                   and not exists (
                       select 1 from {_holds} h
                       where h.sink_id = l.sink_id and h.audit_id = l.audit_id and h.released_at is null));
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", run.Key.SinkId);
        AddScopeParameters(command, run.Key.Scope);
        command.Parameters.AddWithValue("cutoff", run.Cutoff);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<DateTimeOffset> InsertBatchReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRetentionRunRecord run,
        long ordinal,
        int deletedCount,
        IReadOnlyList<SelectedCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var digest = ComputeSelectionDigest(candidates);
        await using var command = new NpgsqlCommand(
            $"""
             insert into {_batches} (
                 run_id, batch_ordinal, sink_id, scope_kind, tenant_id, actor_kind, actor_id,
                 policy_hash, cutoff, batch_size, deleted_count, selected_digest)
             values (@run, @ordinal, @sink, @scope, @tenant, @actor_kind, @actor_id,
                 @policy_hash, @cutoff, @batch_size, @deleted, @digest)
             returning committed_at;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("run", AuditRetentionSemanticNames.RunReceiptId(run.Key));
        command.Parameters.AddWithValue("ordinal", ordinal);
        command.Parameters.AddWithValue("sink", run.Key.SinkId);
        AddScopeParameters(command, run.Key.Scope);
        command.Parameters.AddWithValue("actor_kind", run.Actor.Kind);
        command.Parameters.AddWithValue("actor_id", run.Actor.Id);
        command.Parameters.AddWithValue("policy_hash", run.PolicyHash);
        command.Parameters.AddWithValue("cutoff", run.Cutoff);
        command.Parameters.AddWithValue("batch_size", run.Policy.BatchSize);
        command.Parameters.AddWithValue("deleted", deletedCount);
        command.Parameters.AddWithValue("digest", digest);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            _ => throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit retention batch receipt did not return a timestamp.")
        };
    }

    private async Task<DateTimeOffset> UpdateRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string runId,
        long deletedCount,
        long batchCount,
        AuditRetentionRunState state,
        string? stopReason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             update {_runs}
             set committed_deleted_count = @deleted, committed_batch_count = @batches,
                 state = @state, stop_reason = @stop, updated_at = clock_timestamp()
             where run_id = @run
             returning updated_at;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("deleted", deletedCount);
        command.Parameters.AddWithValue("batches", batchCount);
        command.Parameters.AddWithValue("state", (int)state);
        if (stopReason is null)
            command.Parameters.Add("stop", NpgsqlTypes.NpgsqlDbType.Text).Value = DBNull.Value;
        else
            command.Parameters.AddWithValue("stop", stopReason);
        command.Parameters.AddWithValue("run", runId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            _ => throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Audit retention run update did not return a timestamp.")
        };
    }

    private async Task<(int DeletedCount, DateTimeOffset CommittedAt)?> ReadBatchReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string runId,
        long ordinal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select deleted_count, committed_at from {_batches} where run_id=@run and batch_ordinal=@ordinal;",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("ordinal", ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return (reader.GetInt32(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<string?> ReadLedgerBodyStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRetentionScope scope,
        string auditId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select body_state from {_ledger} where sink_id=@sink and scope_kind=@scope and tenant_id=@tenant and audit_id=@audit for update;",
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("sink", SinkIdValue);
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("audit", auditId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value as string;
    }

    private async Task<AuditHold?> ReadHoldForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string holdId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             select hold_id, sink_id, audit_id, scope_kind, tenant_id, reason_code,
                    actor_kind, actor_id, actor_display_name, correlation_id, version,
                    created_at, released_at
             from {_holds} where hold_id=@hold for update;
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("hold", holdId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new AuditHold
        {
            HoldId = reader.GetString(0),
            StoreId = StoreIdValue,
            SinkId = reader.GetString(1),
            AuditId = reader.GetString(2),
            Scope = ReadScope(reader, 3, 4),
            ReasonCode = reader.GetString(5),
            Actor = new AuditActor
            {
                Kind = reader.GetString(6),
                Id = reader.GetString(7),
                DisplayName = reader.IsDBNull(8) ? null : reader.GetString(8)
            },
            CorrelationId = reader.GetString(9),
            Version = reader.GetInt64(10),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(11),
            ReleasedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)
        };
    }

    private async Task InsertHoldReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string holdId,
        long version,
        string action,
        AuditRetentionScope scope,
        string auditId,
        string reasonCode,
        AuditActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
             insert into {_holdReceipts} (
                 hold_id, version, action, sink_id, audit_id, scope_kind, tenant_id, reason_code,
                 actor_kind, actor_id, correlation_id)
             values (@hold, @version, @action, @sink, @audit, @scope, @tenant, @reason,
                 @actor_kind, @actor_id, @correlation);
             """,
            connection,
            transaction)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("hold", holdId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("sink", SinkIdValue);
        command.Parameters.AddWithValue("audit", auditId);
        AddScopeParameters(command, scope);
        command.Parameters.AddWithValue("reason", reasonCode);
        command.Parameters.AddWithValue("actor_kind", actor.Kind);
        command.Parameters.AddWithValue("actor_id", actor.Id);
        command.Parameters.AddWithValue("correlation", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeSelectionDigest(IReadOnlyList<SelectedCandidate> candidates)
    {
        var builder = new StringBuilder();
        foreach (var candidate in candidates)
        {
            builder.Append(candidate.AuditId).Append('\u001f').Append(candidate.IntegrityJson).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static AuditRetentionRunRecord ToRecord(
        AuditRetentionRunRequest request,
        string runId,
        string policyHash,
        AuditRetentionRunState state,
        long deleted,
        long batches,
        string? stopReason,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        => new()
        {
            Key = new AuditRetentionRunKey
            {
                StoreId = request.StoreId,
                SinkId = request.SinkId,
                Scope = request.Scope,
                RequestId = request.RequestId
            },
            Policy = request.Policy,
            PolicyHash = policyHash,
            SnapshotAt = request.SnapshotAt,
            Cutoff = request.Cutoff,
            ReasonCode = request.ReasonCode,
            Actor = request.Actor,
            CorrelationId = request.CorrelationId,
            State = state,
            CommittedDeletedCount = deleted,
            CommittedBatchCount = batches,
            StopReason = stopReason,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

    private sealed record SelectedCandidate(string AuditId, string IntegrityJson);
}
