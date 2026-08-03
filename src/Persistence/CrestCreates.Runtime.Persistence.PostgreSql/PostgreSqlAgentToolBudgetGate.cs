using System.Text.Json;
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

    private async ValueTask<AgentToolBudgetReserveResult> ReserveCoreAsync(AgentToolBudgetReserveRequest request, CancellationToken cancellationToken)
    {
        var budget = request.Context.Governance.Budget;
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

        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            insert into {_options.Schema}.agent_tool_budget_reservations
                (tenant_id, reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state)
            values (@tenantId, @reservationId, @attemptId, @fp, @category, @costUnits, @maxCalls, @state)
            on conflict (tenant_id, attempt_id) do nothing
            returning reservation_id
            """;
        var tenantId = request.Context.LogicalInvocationKey.TenantId ?? string.Empty;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", reservation.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", reservation.AttemptId));
        cmd.Parameters.Add(new NpgsqlParameter("fp", reservation.InvocationFingerprint));
        cmd.Parameters.Add(new NpgsqlParameter("category", reservation.Category));
        cmd.Parameters.Add(new NpgsqlParameter("costUnits", reservation.CostUnits));
        cmd.Parameters.Add(new NpgsqlParameter("maxCalls", (object?)reservation.MaxCallsPerExecution ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("state", (int)reservation.State));

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Denied, Reservation = null };
        }

        return new AgentToolBudgetReserveResult { Status = AgentToolBudgetReserveStatus.Reserved, Reservation = reservation };
    }

    private async ValueTask<AgentToolBudgetReservation> FinalizeCoreAsync(AgentToolBudgetFinalizeRequest request, CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            update {_options.Schema}.agent_tool_budget_reservations
            set state = @state, updated_at = clock_timestamp()
            where reservation_id = @reservationId
            returning reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state
            """;
        cmd.Parameters.Add(new NpgsqlParameter("reservationId", request.ReservationId));
        cmd.Parameters.Add(new NpgsqlParameter("state", (int)AgentToolBudgetReservationState.Committed));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Budget reservation {request.ReservationId} not found.");

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

    private async ValueTask<AgentToolBudgetReservationReadResult> GetReservationStateCoreAsync(AgentToolPreDispatchIdentity identity, CancellationToken cancellationToken)
    {
        var connection = _coordinator.RequireSession().Connection;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            select reservation_id, attempt_id, invocation_fingerprint, category, cost_units, max_calls_per_execution, state
            from {_options.Schema}.agent_tool_budget_reservations
            where tenant_id = @tenantId and attempt_id = @attemptId
            """;
        cmd.Parameters.Add(new NpgsqlParameter("tenantId", identity.LogicalInvocationKey.TenantId ?? string.Empty));
        cmd.Parameters.Add(new NpgsqlParameter("attemptId", identity.AttemptId));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new AgentToolBudgetReservationReadResult { Status = AgentToolBudgetReadStatus.Missing };

        var reservation = new AgentToolBudgetReservation
        {
            ReservationId = reader.GetString(0),
            AttemptId = reader.GetString(1),
            InvocationFingerprint = reader.GetString(2),
            Category = reader.GetString(3),
            CostUnits = reader.GetInt64(4),
            MaxCallsPerExecution = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            State = (AgentToolBudgetReservationState)reader.GetInt32(6)
        };

        var status = reservation.State switch
        {
            AgentToolBudgetReservationState.Reserved => AgentToolBudgetReadStatus.Reserved,
            AgentToolBudgetReservationState.Released => AgentToolBudgetReadStatus.Released,
            AgentToolBudgetReservationState.Committed => AgentToolBudgetReadStatus.Committed,
            _ => AgentToolBudgetReadStatus.Indeterminate
        };

        return new AgentToolBudgetReservationReadResult { Status = status, Reservation = reservation };
    }
}
