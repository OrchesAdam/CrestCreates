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

        var accountabilityFloor = _options.AccountabilityProjectionRetryWindow;
        var cutoffCheckpoint = now - Max(_options.GovernanceCheckpointRetention, accountabilityFloor);
        var cutoffBudget = now - Max(_options.BudgetReservationRetention, accountabilityFloor);
        var cutoffReceipt = now - Max(_options.InvocationAttemptReceiptRetention, accountabilityFloor);
        var cutoffObservation = now - _options.ReconciliationObservationRetention;
        var cutoffReconciliationReceipt = now - _options.ReconciliationReceiptRetention;
        var cutoffFinalization = now - Max(_options.GovernanceFinalizationRetention, accountabilityFloor);
        var cutoffReconciliationWindow = now - _options.MaximumInvocationReconciliationWindow;

        var terminalAttemptStates = string.Join(", ",
            (int)AgentToolInvocationPreDispatchState.Abandoned,
            (int)AgentToolInvocationPreDispatchState.Released,
            (int)AgentToolInvocationPreDispatchState.Completed);
        var terminalBudgetStates = string.Join(", ",
            (int)AgentToolBudgetReservationState.Released,
            (int)AgentToolBudgetReservationState.Committed,
            (int)AgentToolBudgetReservationState.Indeterminate);

        // A checkpoint is removable only when every linked authority proves that
        // the exact tenant/logical-key/Attempt aggregate is terminal.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_pre_dispatch_checkpoints c
            WHERE c.accepted_at < @cutoff_checkpoint
              AND EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = c.tenant_id
                    AND i.logical_invocation_key = c.logical_invocation_key
                    AND i.attempt_id = c.attempt_id
                    AND i.pre_dispatch_state IN ({terminalAttemptStates}))
              AND EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_governance_finalizations f
                  WHERE f.tenant_id = c.tenant_id
                    AND f.logical_invocation_key = c.logical_invocation_key
                    AND f.attempt_id = c.attempt_id)
              AND NOT EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_budget_reservations b
                  WHERE b.tenant_id = c.tenant_id
                    AND b.logical_invocation_key = c.logical_invocation_key
                    AND b.attempt_id = c.attempt_id
                    AND b.state NOT IN ({terminalBudgetStates}))
              AND NOT EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = c.tenant_id
                    AND o.logical_invocation_key = c.logical_invocation_key
                    AND o.attempt_id = c.attempt_id)
              AND NOT EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_reconciliation_receipts r
                  WHERE r.tenant_id = c.tenant_id
                    AND r.logical_invocation_key = c.logical_invocation_key
                    AND r.attempt_id = c.attempt_id
                    AND r.terminal_at >= @cutoff_reconciliation_window)
            """,
            ("cutoff_checkpoint", cutoffCheckpoint),
            ("cutoff_reconciliation_window", cutoffReconciliationWindow));

        // Finalization and decision rows use the complete identity. AttemptId
        // alone is intentionally insufficient because it is provider-owned text.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_governance_finalizations f
            WHERE f.created_at < @cutoff_finalization
              AND EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = f.tenant_id
                    AND i.logical_invocation_key = f.logical_invocation_key
                    AND i.attempt_id = f.attempt_id
                    AND i.pre_dispatch_state IN ({terminalAttemptStates}))
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_pre_dispatch_checkpoints c
                  WHERE c.tenant_id = f.tenant_id
                    AND c.logical_invocation_key = f.logical_invocation_key
                    AND c.attempt_id = f.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = f.tenant_id
                    AND o.logical_invocation_key = f.logical_invocation_key
                    AND o.attempt_id = f.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_receipts r
                  WHERE r.tenant_id = f.tenant_id
                    AND r.logical_invocation_key = f.logical_invocation_key
                    AND r.attempt_id = f.attempt_id
                    AND r.terminal_at >= @cutoff_reconciliation_window)
            """,
            ("cutoff_finalization", cutoffFinalization),
            ("cutoff_reconciliation_window", cutoffReconciliationWindow));
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_governance_decisions d
            WHERE d.created_at < @cutoff_finalization
              AND EXISTS (
                  SELECT 1
                  FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = d.tenant_id
                    AND i.logical_invocation_key = d.logical_invocation_key
                    AND i.attempt_id = d.attempt_id
                    AND i.pre_dispatch_state IN ({terminalAttemptStates}))
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = d.tenant_id
                    AND o.logical_invocation_key = d.logical_invocation_key
                    AND o.attempt_id = d.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_receipts r
                  WHERE r.tenant_id = d.tenant_id
                    AND r.logical_invocation_key = d.logical_invocation_key
                    AND r.attempt_id = d.attempt_id
                    AND r.terminal_at >= @cutoff_reconciliation_window)
            """,
            ("cutoff_finalization", cutoffFinalization),
            ("cutoff_reconciliation_window", cutoffReconciliationWindow));

        // A terminal budget cannot age out while its Gate is live or reconciliation
        // still has a mutable observation/new terminal receipt.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_budget_reservations b
            WHERE b.created_at < @cutoff_budget
              AND b.state IN ({terminalBudgetStates})
              AND EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = b.tenant_id
                    AND i.logical_invocation_key = b.logical_invocation_key
                    AND i.attempt_id = b.attempt_id
                    AND i.pre_dispatch_state IN ({terminalAttemptStates}))
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = b.tenant_id
                    AND o.logical_invocation_key = b.logical_invocation_key
                    AND o.attempt_id = b.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_receipts r
                  WHERE r.tenant_id = b.tenant_id
                    AND r.logical_invocation_key = b.logical_invocation_key
                    AND r.attempt_id = b.attempt_id
                    AND r.terminal_at >= @cutoff_reconciliation_window)
            """,
            ("cutoff_budget", cutoffBudget),
            ("cutoff_reconciliation_window", cutoffReconciliationWindow));

        // Delete only named terminal states, after all aggregate-owned evidence is
        // gone. Unknown and DispatchStarted are not terminal cleanup candidates.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
            WHERE i.created_at < @cutoff_receipt
              AND i.pre_dispatch_state IN ({terminalAttemptStates})
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_pre_dispatch_checkpoints c
                  WHERE c.tenant_id = i.tenant_id AND c.logical_invocation_key = i.logical_invocation_key AND c.attempt_id = i.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_budget_reservations b
                  WHERE b.tenant_id = i.tenant_id AND b.logical_invocation_key = i.logical_invocation_key AND b.attempt_id = i.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_governance_finalizations f
                  WHERE f.tenant_id = i.tenant_id AND f.logical_invocation_key = i.logical_invocation_key AND f.attempt_id = i.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_governance_decisions d
                  WHERE d.tenant_id = i.tenant_id AND d.logical_invocation_key = i.logical_invocation_key AND d.attempt_id = i.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = i.tenant_id AND o.logical_invocation_key = i.logical_invocation_key AND o.attempt_id = i.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_receipts r
                  WHERE r.tenant_id = i.tenant_id
                    AND r.logical_invocation_key = i.logical_invocation_key
                    AND r.attempt_id = i.attempt_id
                    AND r.terminal_at >= @cutoff_reconciliation_window)
            """,
            ("cutoff_receipt", cutoffReceipt),
            ("cutoff_reconciliation_window", cutoffReconciliationWindow));

        // StillPending is mutable retry state and therefore never age-deleted.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_reconciliation_observations
            WHERE observed_at < @cutoff_observation
              AND status != {(int)AgentToolPreDispatchReconciliationStatus.StillPending}
            """,
            ("cutoff_observation", cutoffObservation));

        // A terminal tombstone outlives the aggregate and disappears only after
        // its own retention once neither Gate nor mutable observation remains.
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_reconciliation_receipts r
            WHERE r.terminal_at < @cutoff_reconciliation_receipt
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_invocation_pre_dispatch i
                  WHERE i.tenant_id = r.tenant_id AND i.logical_invocation_key = r.logical_invocation_key AND i.attempt_id = r.attempt_id)
              AND NOT EXISTS (
                  SELECT 1 FROM {_options.Schema}.agent_tool_reconciliation_observations o
                  WHERE o.tenant_id = r.tenant_id AND o.logical_invocation_key = r.logical_invocation_key AND o.attempt_id = r.attempt_id)
            """,
            ("cutoff_reconciliation_receipt", cutoffReconciliationReceipt));

        return totalDeleted;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
        => left >= right ? left : right;

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
