using CrestCreates.Agent.Tools;
using CrestCreates.Metadata.AgentTool;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolBudgetGate : IAgentToolBudgetGate
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;

    public PostgreSqlAgentToolBudgetGate(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => ReserveCoreAsync(request, ct), cancellationToken);

    public ValueTask<AgentToolBudgetReservation> FinalizeAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => FinalizeCoreAsync(request, ct), cancellationToken);

    public ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => GetReservationStateCoreAsync(identity, ct), cancellationToken);

    private NpgsqlConnection Conn() => _coordinator.RequireSession().Connection;

    private static NpgsqlParameter IntParam(string name, int value)
        => new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter JsonParam(string name, string json)
        => new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json };

    private async ValueTask<AgentToolBudgetReserveResult> ReserveCoreAsync(AgentToolBudgetReserveRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = request.Context;

        // Phase 8f contract: validate the governance context first.
        if (!AgentToolGovernanceGuard.IsValid(context))
            return Denied("budget_context_invalid");

        var requirement = context.Governance.Budget;
        if (requirement is null
            || string.IsNullOrWhiteSpace(requirement.Category)
            || requirement.CostUnits <= 0
            || requirement.MaxCallsPerExecution is <= 0)
        {
            return Denied("budget_requirement_invalid");
        }

        var tenantId = context.LogicalInvocationKey.TenantId ?? string.Empty;
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            context.LogicalInvocationKey,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);
        var toolContractJson = PostgreSqlRuntimeStoreSupport.Serialize(
            context.ToolContract,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolContractIdentity);
        var capacityKey = BuildCapacityKey(context);
        var reservation = new AgentToolBudgetReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            AttemptId = context.AttemptId,
            InvocationFingerprint = context.InvocationFingerprint ?? string.Empty,
            Category = requirement.Category,
            CostUnits = requirement.CostUnits,
            MaxCallsPerExecution = requirement.MaxCallsPerExecution,
            State = AgentToolBudgetReservationState.Reserved
        };

        // Serialize reserves that share a logical invocation and a capacity key so the
        // read-then-insert conflict and capacity counting are race-free (the advisory
        // lock is released when the surrounding transaction commits or rolls back).
        if (requirement.MaxCallsPerExecution is { } maxCalls)
        {
            await TakeAdvisoryXactLockAsync(LogicalInvocationLockKey(logicalKeyJson), ct).ConfigureAwait(false);
            await TakeAdvisoryXactLockAsync(CapacityLockKey(tenantId, capacityKey), ct).ConfigureAwait(false);

            // Attempt-idempotent: the same Attempt already has a reservation.
            var existingAttempt = await ReadReservationByIdentityAsync(tenantId, logicalKeyJson, context.AttemptId, ct)
                .ConfigureAwait(false);
            if (existingAttempt is not null)
            {
                if (!MatchesAttempt(existingAttempt, context, requirement, toolContractJson))
                    return Denied("budget_attempt_conflict");
                return existingAttempt.Reservation.State == AgentToolBudgetReservationState.Reserved
                    ? Reserved(existingAttempt.Reservation)
                    : Denied("budget_attempt_already_finalized");
            }

            // Logical invocation conflicts: same LogicalInvocationKey must agree on
            // ToolContract and InvocationFingerprint, and must not be Committed/Indeterminate.
            var logicalReservations = await ReadReservationsByLogicalKeyAsync(tenantId, logicalKeyJson, ct)
                .ConfigureAwait(false);
            if (logicalReservations.Any(entry =>
                    !PostgreSqlRuntimeStoreSupport.JsonEquals(entry.ToolContractJson, toolContractJson)
                    || !string.Equals(
                        entry.Reservation.InvocationFingerprint,
                        context.InvocationFingerprint,
                        StringComparison.Ordinal)))
            {
                return Denied("budget_logical_invocation_conflict");
            }

            if (logicalReservations.Any(entry =>
                    entry.Reservation.State == AgentToolBudgetReservationState.Committed))
            {
                return Denied("budget_logical_invocation_committed");
            }

            if (logicalReservations.Any(entry =>
                    entry.Reservation.State == AgentToolBudgetReservationState.Indeterminate))
            {
                return Denied("budget_logical_invocation_indeterminate");
            }

            // Capacity: count Reserved/Committed/Indeterminate for the same capacity key.
            var occupied = await CountOccupiedCapacityAsync(tenantId, capacityKey, ct).ConfigureAwait(false);
            if (occupied >= maxCalls)
                return Denied("budget_capacity_exceeded");
        }
        else
        {
            // No capacity bound — still enforce attempt idempotency before inserting.
            var existingAttempt = await ReadReservationByIdentityAsync(tenantId, logicalKeyJson, context.AttemptId, ct)
                .ConfigureAwait(false);
            if (existingAttempt is not null)
            {
                if (!MatchesAttempt(existingAttempt, context, requirement, toolContractJson))
                    return Denied("budget_attempt_conflict");
                return existingAttempt.Reservation.State == AgentToolBudgetReservationState.Reserved
                    ? Reserved(existingAttempt.Reservation)
                    : Denied("budget_attempt_already_finalized");
            }
        }

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_budget_reservations
                (tenant_id, reservation_id, attempt_id, logical_invocation_key, invocation_fingerprint,
                 category, cost_units, max_calls_per_execution, state, tool_contract_json, capacity_key)
            values (@tenantId, @reservationId, @attemptId, @lik, @fp, @category, @costUnits, @maxCalls, @state, @tcj, @ck)
            on conflict (tenant_id, attempt_id) do nothing
            returning reservation_id
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", reservation.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", reservation.AttemptId));
        cmd.Parameters.Add(JsonParam("lik", logicalKeyJson));
        cmd.Parameters.Add(new NpgsqlParameter("fp", reservation.InvocationFingerprint));
        cmd.Parameters.Add(new NpgsqlParameter("category", reservation.Category));
        cmd.Parameters.Add(new NpgsqlParameter("costUnits", reservation.CostUnits));
        cmd.Parameters.Add(new NpgsqlParameter("maxCalls", (object?)reservation.MaxCallsPerExecution ?? DBNull.Value));
        cmd.Parameters.Add(IntParam("state", (int)reservation.State));
        cmd.Parameters.Add(JsonParam("tcj", toolContractJson));
        cmd.Parameters.Add(new NpgsqlParameter("ck", capacityKey));

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not null)
            return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Reserved, Reservation = reservation };

        // Lost insert race: another worker reserved the same Attempt between read and insert.
        var raced = await ReadReservationByIdentityAsync(tenantId, logicalKeyJson, context.AttemptId, ct)
            .ConfigureAwait(false);
        if (raced is null)
            return Denied("budget_attempt_conflict");
        if (!MatchesAttempt(raced, context, requirement, toolContractJson))
            return Denied("budget_attempt_conflict");
        return raced.Reservation.State == AgentToolBudgetReservationState.Reserved
            ? Reserved(raced.Reservation)
            : Denied("budget_attempt_already_finalized");
    }

    private async ValueTask<AgentToolBudgetReservation> FinalizeCoreAsync(AgentToolBudgetFinalizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReservationId)
            || string.IsNullOrWhiteSpace(request.AttemptId)
            || string.IsNullOrWhiteSpace(request.InvocationFingerprint)
            || string.IsNullOrWhiteSpace(request.ReasonCode)
            || request.RequestedState is not (
                AgentToolBudgetReservationState.Released
                or AgentToolBudgetReservationState.Committed
                or AgentToolBudgetReservationState.Indeterminate))
        {
            throw new ArgumentException("Budget finalization has an invalid contract.", nameof(request));
        }

        var targetState = request.RequestedState;
        await using var cmd = Conn().CreateCommand();
        // Terminal monotonicity: only allow finalizing from Reserved (non-terminal → terminal).
        // Once terminal, the reservation is immutable.
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_budget_reservations
            set state = @state, updated_at = clock_timestamp()
            where tenant_id = @tenantId
              and reservation_id = @reservationId
              and attempt_id = @attemptId
              and invocation_fingerprint = @fp
              and state = {(int)AgentToolBudgetReservationState.Reserved}
            returning reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", request.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", request.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", request.AttemptId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("fp", request.InvocationFingerprint ?? string.Empty));
        cmd.Parameters.Add(IntParam("state", (int)targetState));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Close the reader before running a follow-up query on the same connection.
            await reader.DisposeAsync().ConfigureAwait(false);
            // Either not found, identity mismatch, or already terminal.
            // Read current state to distinguish.
            var existing = await ReadReservationByIdReservationIdAsync(request.ReservationId, request.TenantId, ct);
            if (existing is null)
                throw new InvalidOperationException($"Budget reservation {request.ReservationId} not found.");
            if (!string.Equals(existing.Reservation.AttemptId, request.AttemptId, StringComparison.Ordinal)
                || !string.Equals(
                    existing.Reservation.InvocationFingerprint,
                    request.InvocationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The budget reservation identity does not match.");
            }
            // Already terminal — only idempotent if the existing state matches the requested state.
            if (existing.Reservation.State == targetState)
                return existing.Reservation;
            // Different terminal state — conflict, not idempotent success.
            throw new InvalidOperationException(
                $"Budget reservation {request.ReservationId} is in terminal state {existing.Reservation.State}, cannot finalize to {targetState}.");
        }

        return ReadReservation(reader);
    }

    private async ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateCoreAsync(AgentToolPreDispatchIdentity identity, CancellationToken ct)
    {
        var logicalKeyJson = PostgreSqlRuntimeStoreSupport.Serialize(
            identity.LogicalInvocationKey,
            PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey);
        var reservation = await ReadReservationByIdentityAsync(
            identity.LogicalInvocationKey.TenantId ?? string.Empty,
            logicalKeyJson,
            identity.AttemptId,
            ct);
        if (reservation is null)
            return new AgentToolBudgetReservationReadResult { Status = AgentToolBudgetReadStatus.Missing };

        var status = reservation.Reservation.State switch
        {
            AgentToolBudgetReservationState.Reserved => AgentToolBudgetReadStatus.Reserved,
            AgentToolBudgetReservationState.Released => AgentToolBudgetReadStatus.Released,
            AgentToolBudgetReservationState.Committed => AgentToolBudgetReadStatus.Committed,
            _ => AgentToolBudgetReadStatus.Indeterminate
        };

        return new AgentToolBudgetReservationReadResult { Status = status, Reservation = reservation.Reservation };
    }

    private async ValueTask<StoredReservation?> ReadReservationByIdReservationIdAsync(string reservationId, string? tenantId, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state,
                   tool_contract_json, capacity_key
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId
              and reservation_id = @rid
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("rid", reservationId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadStoredReservation(reader);
    }

    private async ValueTask<StoredReservation?> ReadReservationByIdentityAsync(
        string tenantId,
        string logicalKeyJson,
        string attemptId,
        CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state,
                   tool_contract_json, capacity_key
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId
              and logical_invocation_key = @logicalKey
              and attempt_id = @attemptId
            order by created_at desc
            limit 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(JsonParam("logicalKey", logicalKeyJson));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", attemptId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadStoredReservation(reader);
    }

    private async ValueTask<IReadOnlyList<StoredReservation>> ReadReservationsByLogicalKeyAsync(
        string tenantId,
        string logicalKeyJson,
        CancellationToken ct)
    {
        var results = new List<StoredReservation>();
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state,
                   tool_contract_json, capacity_key
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId
              and logical_invocation_key = @logicalKey
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(JsonParam("logicalKey", logicalKeyJson));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadStoredReservation(reader));
        return results;
    }

    private async ValueTask<long> CountOccupiedCapacityAsync(string tenantId, string capacityKey, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select count(*)
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId
              and capacity_key = @capacityKey
              and state in (
                  {(int)AgentToolBudgetReservationState.Reserved},
                  {(int)AgentToolBudgetReservationState.Committed},
                  {(int)AgentToolBudgetReservationState.Indeterminate})
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("capacityKey", capacityKey));
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    private async ValueTask TakeAdvisoryXactLockAsync(long key, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "select pg_advisory_xact_lock(@key)";
        cmd.Parameters.Add(new NpgsqlParameter("key", key));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool MatchesAttempt(
        StoredReservation existing,
        AgentToolGovernanceContext context,
        AgentToolBudgetRequirement requirement,
        string toolContractJson)
        => string.Equals(existing.Reservation.InvocationFingerprint, context.InvocationFingerprint, StringComparison.Ordinal)
            && string.Equals(existing.Reservation.Category, requirement.Category, StringComparison.Ordinal)
            && existing.Reservation.CostUnits == requirement.CostUnits
            && existing.Reservation.MaxCallsPerExecution == requirement.MaxCallsPerExecution
            && PostgreSqlRuntimeStoreSupport.JsonEquals(existing.ToolContractJson, toolContractJson);

    private static string BuildCapacityKey(AgentToolGovernanceContext context)
    {
        var key = context.LogicalInvocationKey;
        var tool = context.ToolContract;
        return string.Join('|',
            key.TenantId ?? string.Empty,
            key.UserId,
            key.AgentId,
            key.ExecutionId,
            tool.Id,
            tool.Version,
            tool.ContractHash,
            context.Governance.Budget.Category);
    }

    // Deterministic, cross-process stable FNV-1a 64-bit hash (advisory lock keys must
    // be identical across processes; string.GetHashCode is per-process randomized).
    private static long LogicalInvocationLockKey(string logicalKeyJson)
        => unchecked((long)Fnv1a64(System.Text.Encoding.UTF8.GetBytes(logicalKeyJson)));

    private static long CapacityLockKey(string tenantId, string capacityKey)
        => unchecked((long)Fnv1a64(System.Text.Encoding.UTF8.GetBytes($"{tenantId}|{capacityKey}")));

    private static ulong Fnv1a64(byte[] data)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    private static AgentToolBudgetReserveResult Denied(string reasonCode)
        => new()
        {
            Status = AgentToolBudgetReserveStatus.Denied,
            ReasonCode = reasonCode
        };

    private static AgentToolBudgetReserveResult Reserved(AgentToolBudgetReservation reservation)
        => new()
        {
            Status = AgentToolBudgetReserveStatus.Reserved,
            Reservation = reservation
        };

    private static StoredReservation ReadStoredReservation(System.Data.Common.DbDataReader reader)
    {
        var reservation = ReadReservation(reader);
        var toolContractJson = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
        var capacityKey = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
        return new StoredReservation(reservation, toolContractJson, capacityKey);
    }

    private static AgentToolBudgetReservation ReadReservation(System.Data.Common.DbDataReader reader)
    {
        return new AgentToolBudgetReservation
        {
            ReservationId = reader.GetString(0),
            AttemptId = reader.GetString(1),
            InvocationFingerprint = reader.GetString(2),
            Category = reader.GetString(3),
            CostUnits = reader.GetInt64(4),
            MaxCallsPerExecution = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            State = (AgentToolBudgetReservationState)reader.GetInt32(6)
        };
    }

    private sealed record StoredReservation(
        AgentToolBudgetReservation Reservation,
        string ToolContractJson,
        string CapacityKey);
}
