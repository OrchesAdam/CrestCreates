using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlAgentToolPreDispatchCleanup
{
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlAgentToolPreDispatchCleanup(
        PostgreSqlRuntimeTransactionCoordinator coordinator,
        PostgreSqlRuntimePersistenceOptions options,
        TimeProvider? timeProvider = null)
    {
        _coordinator = coordinator;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes retention-based cleanup for Agent Tool pre-dispatch tables.
    /// Protected states (Pending, Ready, Accepted, ReleasePending, CompletionPending, Indeterminate)
    /// are never cleaned up. StillPending observations are never cleaned up.
    /// Terminal receipt tombstones are retained independently after aggregate cleanup.
    /// </summary>
    public async ValueTask<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        return await _coordinator.ExecuteAsync(ct => new ValueTask<int>(CleanupCoreAsync(ct)), cancellationToken);
    }

    private async Task<int> CleanupCoreAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var session = _coordinator.RequireSession();
        var totalDeleted = 0;

        var cutoffCheckpoint = now - _options.GovernanceCheckpointRetention;
        var cutoffBudget = now - _options.BudgetReservationRetention;
        var cutoffReceipt = now - _options.InvocationAttemptReceiptRetention;
        var cutoffObservation = now - _options.ReconciliationObservationRetention;
        var cutoffReconciliationReceipt = now - _options.ReconciliationReceiptRetention;
        var cutoffFinalization = now - _options.GovernanceFinalizationRetention;

        // Protected pre-dispatch states that must never be cleaned up
        var protectedStates = string.Join(", ",
            (int)AgentToolInvocationPreDispatchState.Pending,
            (int)AgentToolInvocationPreDispatchState.Ready,
            (int)AgentToolInvocationPreDispatchState.Accepted,
            (int)AgentToolInvocationPreDispatchState.ReleasePending,
            (int)AgentToolInvocationPreDispatchState.CompletionPending,
            (int)AgentToolInvocationPreDispatchState.Indeterminate);

        // 1. Clean up terminal governance checkpoints — only for attempts that have
        //    reached a true terminal state (Released, Completed, Abandoned) in the
        //    invocation gate. Indeterminate is PROTECTED and must NOT be cleaned up.
        //    Must run BEFORE deleting gate rows so the terminal-state check can see them.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_pre_dispatch_checkpoints
            WHERE accepted_at < @cutoff_checkpoint
              AND tenant_id IN (
                  SELECT i.tenant_id
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = agent_tool_pre_dispatch_checkpoints.tenant_id
                    AND i.attempt_id = agent_tool_pre_dispatch_checkpoints.attempt_id
                    AND i.logical_invocation_key = agent_tool_pre_dispatch_checkpoints.logical_invocation_key
                    AND i.pre_dispatch_state IN (
                        {(int)AgentToolInvocationPreDispatchState.Released},
                        {(int)AgentToolInvocationPreDispatchState.Completed},
                        {(int)AgentToolInvocationPreDispatchState.Abandoned}
                    )
              )
            """,
            ("cutoff_checkpoint", cutoffCheckpoint));

        // 2. Clean up terminal governance finalizations and decisions — only for
        //    attempts whose invocation gate has reached a true terminal state.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_governance_finalizations
            WHERE created_at < @cutoff_finalization
              AND attempt_id IN (
                  SELECT i.attempt_id
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.attempt_id = agent_tool_governance_finalizations.attempt_id
                    AND i.pre_dispatch_state IN (
                        {(int)AgentToolInvocationPreDispatchState.Released},
                        {(int)AgentToolInvocationPreDispatchState.Completed},
                        {(int)AgentToolInvocationPreDispatchState.Abandoned}
                    )
              )
            """,
            ("cutoff_finalization", cutoffFinalization));
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_governance_decisions
            WHERE created_at < @cutoff_finalization
              AND attempt_id IN (
                  SELECT i.attempt_id
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.attempt_id = agent_tool_governance_decisions.attempt_id
                    AND i.pre_dispatch_state IN (
                        {(int)AgentToolInvocationPreDispatchState.Released},
                        {(int)AgentToolInvocationPreDispatchState.Completed},
                        {(int)AgentToolInvocationPreDispatchState.Abandoned}
                    )
              )
            """,
            ("cutoff_finalization", cutoffFinalization));

        // 3. Clean up terminal budget reservations (Released, Committed, Indeterminate)
        //    but protect Reserved (active) reservations
        var terminalBudgetStates = string.Join(", ",
            (int)AgentToolBudgetReservationState.Released,
            (int)AgentToolBudgetReservationState.Committed,
            (int)AgentToolBudgetReservationState.Indeterminate);
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_budget_reservations
            WHERE created_at < @cutoff_budget
              AND state IN ({terminalBudgetStates})
            """,
            ("cutoff_budget", cutoffBudget));

        // 4. Clean up terminal invocation pre-dispatch entries (Abandoned, Released, Completed)
        //    but protect non-terminal states. Runs AFTER checkpoint cleanup so the
        //    terminal-state check in step 1 can still see the gate rows.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_invocation_pre_dispatch
            WHERE created_at < @cutoff_receipt
              AND pre_dispatch_state NOT IN ({protectedStates})
            """,
            ("cutoff_receipt", cutoffReceipt));

        // 5. Clean up mutable reconciliation observations
        //    StillPending observations are protected (non-terminal)
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_reconciliation_observations
            WHERE observed_at < @cutoff_observation
              AND status != {(int)AgentToolPreDispatchReconciliationStatus.StillPending}
            """,
            ("cutoff_observation", cutoffObservation));

        // 6. Clean up terminal reconciliation receipt tombstones
        //    These are retained independently after aggregate cleanup
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_reconciliation_receipts
            WHERE terminal_at < @cutoff_reconciliation_receipt
            """,
            ("cutoff_reconciliation_receipt", cutoffReconciliationReceipt));

        return totalDeleted;
    }

    private static async Task<int> ExecuteNonQueryAsync(PostgreSqlRuntimeSession session, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = session.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.Add(CreateParameter(name, value));
        }
        return await cmd.ExecuteNonQueryAsync();
    }

    private static NpgsqlParameter CreateParameter(string name, object value)
    {
        return value switch
        {
            DateTimeOffset dto => new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = dto },
            DateTime dt => new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = dt },
            _ => new NpgsqlParameter(name, value)
        };
    }
}