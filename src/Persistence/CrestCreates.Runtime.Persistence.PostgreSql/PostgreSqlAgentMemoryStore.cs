using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Agent.Memory.Abstractions.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Durable Candidate/Memory store implementing the base Store contract plus
/// conditional curation and capability surfaces on the same instance.
/// Conditional curation owns a provider-level top-level COMMIT boundary.
/// </summary>
internal sealed class PostgreSqlAgentMemoryStore : IAgentMemoryStore, IAgentMemoryStoreCapabilities, IAgentMemoryConditionalCurationStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly PostgreSqlAgentMemoryLockManager _lockManager;
    private readonly IAgentMemoryStateHashProjector _stateHashes;
    private readonly IAgentMemoryCurationStateMachine _stateMachine;
    private readonly IAgentMemoryPersistenceComparer _comparer;

    public PostgreSqlAgentMemoryStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        PostgreSqlAgentMemoryLockManager lockManager,
        IAgentMemoryStateHashProjector stateHashes,
        IAgentMemoryCurationStateMachine stateMachine,
        IAgentMemoryPersistenceComparer comparer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _stateHashes = stateHashes ?? throw new ArgumentNullException(nameof(stateHashes));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    }

    /// <summary>Truthful capability: ConfirmedAtomic only after all four formal
    /// primitives are implemented atomically (Slice 8).</summary>
    public AgentMemoryCurationOutcomeGuarantee CurationOutcomeGuarantee
        => AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic;

    // ── Candidate base ───────────────────────────────────────────────────────

    public ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => CreateCandidatesCoreAsync([candidate], ct), cancellationToken);

    public ValueTask CreateCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
        => CreateCandidatesAsync([candidate], cancellationToken);

    public ValueTask CreateCandidatesAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => CreateCandidatesCoreAsync(candidates, ct), cancellationToken);

    public ValueTask TransitionCandidateStatusAsync(
        string tenantId,
        string candidateId,
        AgentMemoryStatus expectedStatus,
        AgentMemoryStatus newStatus,
        CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => TransitionCandidateStatusCoreAsync(tenantId, candidateId, expectedStatus, newStatus, ct), cancellationToken);

    public ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetCandidateCoreAsync(tenantId, candidateId, ct), cancellationToken);

    // ── Memory base ──────────────────────────────────────────────────────────

    public ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => SaveMemoryCoreAsync(memory, ct), cancellationToken);

    public ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetMemoryCoreAsync(tenantId, memoryId, ct), cancellationToken);

    public ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ListMemoriesCoreAsync(query, ct), cancellationToken);

    // ── Conditional curation ─────────────────────────────────────────────────

    public ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => PromoteCoreAsync(tenantId, plan, ct), cancellationToken);

    public ValueTask RejectAsync(string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => RejectCoreAsync(tenantId, candidate, operation, ct), cancellationToken);

    public ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => SupersedeCoreAsync(tenantId, plan, ct), cancellationToken);

    public ValueTask<AgentMemoryItem> ArchiveAsync(string tenantId, AgentMemoryItemExpectation memory, AgentMemoryOperationRequest operation, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteTopLevelAsync(ct => ArchiveCoreAsync(tenantId, memory, operation, ct), cancellationToken);

    // ── Candidate implementations ────────────────────────────────────────────

    private async ValueTask CreateCandidatesCoreAsync(IReadOnlyList<AgentMemoryCandidate> candidates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ct.ThrowIfCancellationRequested();
        if (candidates.Count == 0 || candidates.Any(item => item is null))
            throw new ArgumentException("At least one Candidate is required.", nameof(candidates));

        var snapshots = candidates
            .Select(candidate => candidate.Snapshot())
            .ToArray();
        if (snapshots.Select(item => (item.TenantId, item.CandidateId)).Distinct().Count() != snapshots.Length)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Candidate identity already exists.");

        var serialized = snapshots.Select(snapshot => PostgreSqlAgentMemoryStoreSupport.Serialize(
            snapshot, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate)).ToArray();
        var stateHashes = snapshots.Select(snapshot => _stateHashes.ComputeCandidateStateHash(snapshot)).ToArray();

        var session = _coordinator.RequireSession();
        foreach (var group in snapshots.GroupBy(item => item.TenantId, StringComparer.Ordinal))
        {
            await _lockManager.AcquireAsync(
                session,
                group.Key,
                "candidate",
                group.Select(item => item.CandidateId).ToArray(),
                ct).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshots.Length; index++)
        {
            var candidate = snapshots[index];
            await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
                insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
                    (tenant_id, candidate_id, revision, status, kind, canonical_content_hash, state_hash,
                     state_contract_version, state_json, created_at, updated_at)
                values (@tenant, @candidate, 1, @status, @kind, @contentHash, @stateHash, 1, @state,
                        clock_timestamp(), clock_timestamp())
                on conflict (tenant_id, candidate_id) do nothing
                returning candidate_id;
                """);
            command.Parameters.AddWithValue("tenant", candidate.TenantId);
            command.Parameters.AddWithValue("candidate", candidate.CandidateId);
            command.Parameters.AddWithValue("status", (int)candidate.Status);
            command.Parameters.AddWithValue("kind", (int)candidate.Kind);
            command.Parameters.AddWithValue("contentHash", candidate.CanonicalContentHash.Value);
            command.Parameters.AddWithValue("stateHash", stateHashes[index].Value);
            PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized[index]);
            using var lease = session.EnterCommand();
            var inserted = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (inserted is null)
            {
                throw new AgentMemoryOperationException(
                    AgentMemoryOperationFailureCode.IdentityConflict,
                    "Candidate identity already exists.");
            }
        }
    }

    private async ValueTask TransitionCandidateStatusCoreAsync(
        string tenantId, string candidateId, AgentMemoryStatus expectedStatus, AgentMemoryStatus newStatus, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var session = _coordinator.RequireSession();
        await _lockManager.AcquireAsync(session, tenantId, "candidate", [candidateId], ct).ConfigureAwait(false);

        AgentMemoryCandidate? current = null;
        long revision = 0;
        int version = 0;
        string stateJson = string.Empty;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select revision, state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            where tenant_id = @tenant and candidate_id = @candidate
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("candidate", candidateId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");
            revision = reader.GetInt64(0);
            version = reader.GetInt32(1);
            stateJson = reader.GetString(2);
        }

        current = PostgreSqlAgentMemoryRowMapper.MapCandidate(
            tenantId, candidateId, revision, version, 0, string.Empty, 1, stateJson,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate,
            _stateHashes);
        if (current.Status != expectedStatus)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, "Candidate lifecycle state changed.");

        var projected = (current with { Status = newStatus }).Snapshot();
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            projected, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate);
        var stateHash = _stateHashes.ComputeCandidateStateHash(projected);

        await using var update = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            update {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            set status = @status,
                state_json = @state,
                state_hash = @stateHash,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant and candidate_id = @candidate;
            """);
        update.Parameters.AddWithValue("tenant", tenantId);
        update.Parameters.AddWithValue("candidate", candidateId);
        update.Parameters.AddWithValue("status", (int)newStatus);
        update.Parameters.AddWithValue("stateHash", stateHash.Value);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(update, "state", serialized);
        using var lease2 = session.EnterCommand();
        await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentMemoryCandidate?> GetCandidateCoreAsync(string tenantId, string candidateId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, candidate_id, revision, status, kind, canonical_content_hash, state_hash,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            where tenant_id = @tenant and candidate_id = @candidate;
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("candidate", candidateId);
        using var lease = session.EnterCommand();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return PostgreSqlAgentMemoryRowMapper.MapCandidate(
            tenantId,
            candidateId,
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetInt32(7),
            reader.GetString(8),
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate,
            _stateHashes);
    }

    // ── Memory base implementations ──────────────────────────────────────────

    private async ValueTask SaveMemoryCoreAsync(AgentMemoryItem memory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ct.ThrowIfCancellationRequested();

        var snapshot = memory.Snapshot();
        var session = _coordinator.RequireSession();
        await _lockManager.AcquireAsync(session, snapshot.TenantId, "memory", [snapshot.MemoryId], ct).ConfigureAwait(false);

        AgentMemoryItem? existing = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                   canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            where tenant_id = @tenant and memory_id = @memory
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", snapshot.TenantId);
            select.Parameters.AddWithValue("memory", snapshot.MemoryId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                existing = PostgreSqlAgentMemoryRowMapper.MapMemory(
                    snapshot.TenantId,
                    snapshot.MemoryId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetInt32(11),
                    reader.GetString(12),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem,
                    _stateHashes);
            }
        }

        if (existing is not null)
        {
            if (!_comparer.Equals(existing, snapshot))
                throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Memory payload is immutable after creation.");
            return; // exact replay: no UPDATE, no revision change
        }

        if (snapshot.Status != AgentMemoryStatus.Active
            || snapshot.IsAuthoritative
            || snapshot.SupersedesMemoryId is not null
            || snapshot.SupersededByMemoryId is not null)
        {
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.InvalidLifecycleState,
                "A new Memory must be Active, non-authoritative, and unlinked.");
        }

        var stateHash = _stateHashes.ComputeMemoryStateHash(snapshot);
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            snapshot, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem);

        await using var insert = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
                (tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                 canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                 state_contract_version, state_json, created_at, updated_at)
            values (@tenant, @memory, 1, @status, @kind, @confidence, @promotedAt,
                    @contentHash, @stateHash, null, null, 1, @state, clock_timestamp(), clock_timestamp())
            on conflict (tenant_id, memory_id) do nothing
            returning memory_id;
            """);
        insert.Parameters.AddWithValue("tenant", snapshot.TenantId);
        insert.Parameters.AddWithValue("memory", snapshot.MemoryId);
        insert.Parameters.AddWithValue("status", (int)snapshot.Status);
        insert.Parameters.AddWithValue("kind", (int)snapshot.Kind);
        insert.Parameters.AddWithValue("confidence", (int)snapshot.Confidence);
        insert.Parameters.AddWithValue("promotedAt", snapshot.PromotedAt);
        insert.Parameters.AddWithValue("contentHash", snapshot.CanonicalContentHash.Value);
        insert.Parameters.AddWithValue("stateHash", stateHash.Value);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(insert, "state", serialized);
        using var lease2 = session.EnterCommand();
        var inserted = await insert.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (inserted is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.StateConflict, "Memory payload is immutable after creation.");
    }

    private async ValueTask<AgentMemoryItem?> GetMemoryCoreAsync(string tenantId, string memoryId, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                   canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            where tenant_id = @tenant and memory_id = @memory;
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("memory", memoryId);
        using var lease = session.EnterCommand();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var memory = PostgreSqlAgentMemoryRowMapper.MapMemory(
            tenantId,
            memoryId,
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetInt32(11),
            reader.GetString(12),
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem,
            _stateHashes);
        return memory;
    }

    private async ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesCoreAsync(AgentMemoryQuery query, CancellationToken ct)
    {
        var session = _coordinator.RequireSession();
        var statuses = new List<int> { (int)AgentMemoryStatus.Active };
        if (query.IncludeSuperseded) statuses.Add((int)AgentMemoryStatus.Superseded);
        if (query.IncludeArchived) statuses.Add((int)AgentMemoryStatus.Archived);

        var sql = $"""
            select tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                   canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            where tenant_id = @tenant
              and status = any(@statuses)
            """;
        if (query.Kinds.Count > 0)
            sql += " and kind = any(@kinds)";
        if (query.MemoryIds.Count > 0)
            sql += " and memory_id = any(@ids)";
        sql += "\norder by memory_id collate \"C\";";

        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, sql);
        command.Parameters.AddWithValue("tenant", query.TenantId);
        command.Parameters.Add("statuses", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = statuses;
        if (query.Kinds.Count > 0)
            command.Parameters.Add("kinds", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = query.Kinds.Select(kind => (int)kind).ToArray();
        if (query.MemoryIds.Count > 0)
            command.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = query.MemoryIds;

        var records = new List<AgentMemoryItem>();
        using (var lease = session.EnterCommand())
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                records.Add(PostgreSqlAgentMemoryRowMapper.MapMemory(
                    query.TenantId,
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetInt32(11),
                    reader.GetString(12),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem,
                    _stateHashes));
            }
        }

        var filtered = records
            .Where(memory => query.Tags.Count == 0 || query.Tags.Any(tag => memory.Tags.Contains(tag)))
            .Where(memory => query.DescriptorRefs.Count == 0
                || query.DescriptorRefs.Any(reference => memory.DescriptorRefs.Any(item => item.Equals(reference))))
            .OrderBy(memory => memory.MemoryId, StringComparer.Ordinal)
            .Select(memory => memory.Snapshot())
            .ToArray();
        return filtered;
    }

    // ── Conditional curation implementations ─────────────────────────────────

    private async ValueTask<AgentMemoryItem> PromoteCoreAsync(string tenantId, AgentMemoryPromotionPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();
        var session = _coordinator.RequireSession();

        await _lockManager.AcquireAsync(session, tenantId, "memory", [plan.NewMemoryId], ct).ConfigureAwait(false);

        AgentMemoryCandidate? candidate = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, candidate_id, revision, status, kind, canonical_content_hash, state_hash,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            where tenant_id = @tenant and candidate_id = @candidate
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("candidate", plan.Candidate.CandidateId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                candidate = PostgreSqlAgentMemoryRowMapper.MapCandidate(
                    tenantId,
                    plan.Candidate.CandidateId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(7),
                    reader.GetString(8),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate,
                    _stateHashes);
            }
        }
        if (candidate is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");

        var memoryExists = await MemoryExistsAsync(session, tenantId, plan.NewMemoryId, ct).ConfigureAwait(false);
        if (memoryExists)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");

        var mutation = _stateMachine.PreparePromote(tenantId, candidate, plan);
        var candidateSerialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.Candidate, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate);
        var memorySerialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.Memory, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem);
        var candidateHash = _stateHashes.ComputeCandidateStateHash(mutation.Candidate);
        var memoryHash = _stateHashes.ComputeMemoryStateHash(mutation.Memory);

        await InsertMemoryAsync(session, mutation.Memory, memoryHash, memorySerialized, ct).ConfigureAwait(false);
        await UpdateCandidateAsync(session, mutation.Candidate, candidateHash, candidateSerialized, ct).ConfigureAwait(false);

        return mutation.Memory.Snapshot();
    }

    private async ValueTask RejectCoreAsync(
        string tenantId, AgentMemoryCandidateExpectation candidate, AgentMemoryOperationRequest operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ct.ThrowIfCancellationRequested();
        var session = _coordinator.RequireSession();

        AgentMemoryCandidate? current = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, candidate_id, revision, status, kind, canonical_content_hash, state_hash,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            where tenant_id = @tenant and candidate_id = @candidate
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("candidate", candidate.CandidateId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                current = PostgreSqlAgentMemoryRowMapper.MapCandidate(
                    tenantId,
                    candidate.CandidateId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(7),
                    reader.GetString(8),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate,
                    _stateHashes);
            }
        }
        if (current is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Candidate is unavailable.");

        var mutation = _stateMachine.PrepareReject(tenantId, current, candidate);
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.Candidate, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate);
        var stateHash = _stateHashes.ComputeCandidateStateHash(mutation.Candidate);
        await UpdateCandidateAsync(session, mutation.Candidate, stateHash, serialized, ct).ConfigureAwait(false);
    }

    private async ValueTask<AgentMemoryItem> SupersedeCoreAsync(string tenantId, AgentMemorySupersessionPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();
        var session = _coordinator.RequireSession();

        await _lockManager.AcquireAsync(session, tenantId, "memory", [plan.NewMemoryId], ct).ConfigureAwait(false);

        AgentMemoryItem? target = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                   canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            where tenant_id = @tenant and memory_id = @memory
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("memory", plan.TargetMemory.MemoryId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                target = PostgreSqlAgentMemoryRowMapper.MapMemory(
                    tenantId,
                    plan.TargetMemory.MemoryId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetInt32(11),
                    reader.GetString(12),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem,
                    _stateHashes);
            }
        }
        if (target is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Target Memory is unavailable.");

        AgentMemoryCandidate? replacement = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, candidate_id, revision, status, kind, canonical_content_hash, state_hash,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            where tenant_id = @tenant and candidate_id = @candidate
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("candidate", plan.ReplacementCandidate.CandidateId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                replacement = PostgreSqlAgentMemoryRowMapper.MapCandidate(
                    tenantId,
                    plan.ReplacementCandidate.CandidateId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(7),
                    reader.GetString(8),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate,
                    _stateHashes);
            }
        }
        if (replacement is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Replacement Candidate is unavailable.");

        var memoryExists = await MemoryExistsAsync(session, tenantId, plan.NewMemoryId, ct).ConfigureAwait(false);
        if (memoryExists)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.IdentityConflict, "Memory identity conflicts.");

        var mutation = _stateMachine.PrepareSupersede(tenantId, target, replacement, plan);
        var oldSerialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.SupersededMemory, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem);
        var newSerialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.SupersedingMemory, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem);
        var candidateSerialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.ReplacementCandidate, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryCandidate);
        var oldHash = _stateHashes.ComputeMemoryStateHash(mutation.SupersededMemory);
        var newHash = _stateHashes.ComputeMemoryStateHash(mutation.SupersedingMemory);
        var candidateHash = _stateHashes.ComputeCandidateStateHash(mutation.ReplacementCandidate);

        await UpdateMemoryAsync(session, mutation.SupersededMemory, oldHash, oldSerialized, ct).ConfigureAwait(false);
        await InsertMemoryAsync(session, mutation.SupersedingMemory, newHash, newSerialized, ct).ConfigureAwait(false);
        await UpdateCandidateAsync(session, mutation.ReplacementCandidate, candidateHash, candidateSerialized, ct).ConfigureAwait(false);

        return mutation.SupersedingMemory.Snapshot();
    }

    private async ValueTask<AgentMemoryItem> ArchiveCoreAsync(
        string tenantId, AgentMemoryItemExpectation memory, AgentMemoryOperationRequest operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ct.ThrowIfCancellationRequested();
        var session = _coordinator.RequireSession();

        AgentMemoryItem? current = null;
        await using (var select = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                   canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                   state_contract_version, state_json
            from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            where tenant_id = @tenant and memory_id = @memory
            for update;
            """))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("memory", memory.MemoryId);
            using var lease = session.EnterCommand();
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                current = PostgreSqlAgentMemoryRowMapper.MapMemory(
                    tenantId,
                    memory.MemoryId,
                    reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetInt32(11),
                    reader.GetString(12),
                    PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem,
                    _stateHashes);
            }
        }
        if (current is null)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.ResourceUnavailable, "Memory is unavailable.");

        var mutation = _stateMachine.PrepareArchive(tenantId, current, memory);
        var serialized = PostgreSqlAgentMemoryStoreSupport.Serialize(
            mutation.Memory, PostgreSqlRuntimeJsonSerializerContext.Default.AgentMemoryItem);
        var stateHash = _stateHashes.ComputeMemoryStateHash(mutation.Memory);
        await UpdateMemoryAsync(session, mutation.Memory, stateHash, serialized, ct).ConfigureAwait(false);
        return mutation.Memory.Snapshot();
    }

    // ── shared SQL helpers ────────────────────────────────────────────────────

    private async ValueTask<bool> MemoryExistsAsync(PostgreSqlRuntimeSession session, string tenantId, string memoryId, CancellationToken ct)
    {
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            select exists (
                select 1 from {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
                where tenant_id = @tenant and memory_id = @memory);
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("memory", memoryId);
        using var lease = session.EnterCommand();
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private async ValueTask InsertMemoryAsync(
        PostgreSqlRuntimeSession session,
        AgentMemoryItem memory,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash stateHash,
        string serialized,
        CancellationToken ct)
    {
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            insert into {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
                (tenant_id, memory_id, revision, status, kind, confidence, promoted_at,
                 canonical_content_hash, state_hash, supersedes_memory_id, superseded_by_memory_id,
                 state_contract_version, state_json, created_at, updated_at)
            values (@tenant, @memory, 1, @status, @kind, @confidence, @promotedAt,
                    @contentHash, @stateHash, @supersedes, @supersededBy, 1, @state,
                    clock_timestamp(), clock_timestamp());
            """);
        command.Parameters.AddWithValue("tenant", memory.TenantId);
        command.Parameters.AddWithValue("memory", memory.MemoryId);
        command.Parameters.AddWithValue("status", (int)memory.Status);
        command.Parameters.AddWithValue("kind", (int)memory.Kind);
        command.Parameters.AddWithValue("confidence", (int)memory.Confidence);
        command.Parameters.AddWithValue("promotedAt", memory.PromotedAt);
        command.Parameters.AddWithValue("contentHash", memory.CanonicalContentHash.Value);
        command.Parameters.AddWithValue("stateHash", stateHash.Value);
        command.Parameters.AddWithValue("supersedes", (object?)memory.SupersedesMemoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("supersededBy", (object?)memory.SupersededByMemoryId ?? DBNull.Value);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized);
        using var lease = session.EnterCommand();
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async ValueTask UpdateMemoryAsync(
        PostgreSqlRuntimeSession session,
        AgentMemoryItem memory,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash stateHash,
        string serialized,
        CancellationToken ct)
    {
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            update {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memories")}
            set status = @status,
                state_json = @state,
                state_hash = @stateHash,
                supersedes_memory_id = @supersedes,
                superseded_by_memory_id = @supersededBy,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant and memory_id = @memory;
            """);
        command.Parameters.AddWithValue("tenant", memory.TenantId);
        command.Parameters.AddWithValue("memory", memory.MemoryId);
        command.Parameters.AddWithValue("status", (int)memory.Status);
        command.Parameters.AddWithValue("stateHash", stateHash.Value);
        command.Parameters.AddWithValue("supersedes", (object?)memory.SupersedesMemoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("supersededBy", (object?)memory.SupersededByMemoryId ?? DBNull.Value);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized);
        using var lease = session.EnterCommand();
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Memory row disappeared during a locked conditional update.");
    }

    private async ValueTask UpdateCandidateAsync(
        PostgreSqlRuntimeSession session,
        AgentMemoryCandidate candidate,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash stateHash,
        string serialized,
        CancellationToken ct)
    {
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"""
            update {PostgreSqlAgentMemoryStoreSupport.Table(_options, "agent_memory_candidates")}
            set status = @status,
                state_json = @state,
                state_hash = @stateHash,
                revision = revision + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant and candidate_id = @candidate;
            """);
        command.Parameters.AddWithValue("tenant", candidate.TenantId);
        command.Parameters.AddWithValue("candidate", candidate.CandidateId);
        command.Parameters.AddWithValue("status", (int)candidate.Status);
        command.Parameters.AddWithValue("stateHash", stateHash.Value);
        PostgreSqlAgentMemoryStoreSupport.AddJsonParameter(command, "state", serialized);
        using var lease = session.EnterCommand();
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
            throw PostgreSqlAgentMemoryStoreSupport.Invariant("Candidate row disappeared during a locked conditional update.");
    }
}
