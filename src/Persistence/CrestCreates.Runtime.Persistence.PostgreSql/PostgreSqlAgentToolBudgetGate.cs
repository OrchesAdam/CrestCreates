using CrestCreates.Agent.Tools;
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
        var budget = request.Context.Governance.Budget;
        var tenantId = request.Context.LogicalInvocationKey.TenantId ?? string.Empty;
        var reservation = new AgentToolBudgetReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            AttemptId = request.Context.AttemptId,
            InvocationFingerprint = request.Context.InvocationFingerprint ?? string.Empty,
            Category = budget.Category,
            CostUnits = budget.CostUnits,
            MaxCallsPerExecution = budget.MaxCallsPerExecution,
            State = AgentToolBudgetReservationState.Reserved
        };

        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_budget_reservations
                (tenant_id, reservation_id, attempt_id, logical_invocation_key, invocation_fingerprint,
                 category, cost_units, max_calls_per_execution, state)
            values (@tenantId, @reservationId, @attemptId, @lik, @fp, @category, @costUnits, @maxCalls, @state)
            on conflict (tenant_id, attempt_id) do nothing
            returning reservation_id
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", reservation.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", reservation.AttemptId));
        cmd.Parameters.Add(JsonParam("lik", PostgreSqlRuntimeStoreSupport.Serialize(request.Context.LogicalInvocationKey, PostgreSqlRuntimeJsonSerializerContext.Default.AgentToolLogicalInvocationKey)));
        cmd.Parameters.Add(new NpgsqlParameter("fp", reservation.InvocationFingerprint));
        cmd.Parameters.Add(new NpgsqlParameter("category", reservation.Category));
        cmd.Parameters.Add(new NpgsqlParameter("costUnits", reservation.CostUnits));
        cmd.Parameters.Add(new NpgsqlParameter("maxCalls", (object?)reservation.MaxCallsPerExecution ?? DBNull.Value));
        cmd.Parameters.Add(IntParam("state", (int)reservation.State));

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not null)
            return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Reserved, Reservation = reservation };

        // Attempt-idempotent: same AttemptId already has a reservation — recover it
        var existing = await ReadReservationByIdentityAsync(tenantId, reservation.AttemptId, ct);
        if (existing is not null && existing.State == AgentToolBudgetReservationState.Reserved)
            return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Reserved, Reservation = existing };

        // The existing reservation is in a terminal state (Released/Committed/Indeterminate) — cannot re-reserve
        return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Denied, Reservation = existing };
    }

    private async ValueTask<AgentToolBudgetReservation> FinalizeCoreAsync(AgentToolBudgetFinalizeRequest request, CancellationToken ct)
    {
        var targetState = request.RequestedState;
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_budget_reservations
            set state = @state, updated_at = clock_timestamp()
            where reservation_id = @reservationId
            returning reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state
            """;
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", request.ReservationId));
        cmd.Parameters.Add(IntParam("state", (int)targetState));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Budget reservation {request.ReservationId} not found.");

        return ReadReservation(reader);
    }

    private async ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateCoreAsync(AgentToolPreDispatchIdentity identity, CancellationToken ct)
    {
        var reservation = await ReadReservationByIdentityAsync(
            identity.LogicalInvocationKey.TenantId ?? string.Empty, identity.AttemptId, ct);
        if (reservation is null)
            return new AgentToolBudgetReservationReadResult { Status = AgentToolBudgetReadStatus.Missing };

        var status = reservation.State switch
        {
            AgentToolBudgetReservationState.Reserved => AgentToolBudgetReadStatus.Reserved,
            AgentToolBudgetReservationState.Released => AgentToolBudgetReadStatus.Released,
            AgentToolBudgetReservationState.Committed => AgentToolBudgetReadStatus.Committed,
            _ => AgentToolBudgetReadStatus.Indeterminate
        };

        return new AgentToolBudgetReservationReadResult { Status = status, Reservation = reservation };
    }

    private async ValueTask<AgentToolBudgetReservation?> ReadReservationByIdentityAsync(string tenantId, string attemptId, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            select reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId and attempt_id = @attemptId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", attemptId));
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadReservation(reader);
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
}
