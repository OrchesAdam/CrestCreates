using System.Diagnostics.CodeAnalysis;
using CrestCreates.Agent.Tools;
using Npgsql;
using NpgsqlTypes;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Tier 3 persistence provider — cleanup queries use parameterized SQL with no reflection-based serialization.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Tier 3 persistence provider — cleanup queries use parameterized SQL with no reflection-based serialization.")]
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

        // 1. Clean up terminal invocation pre-dispatch entries (Abandoned, Released, Completed)
        //    but protect non-terminal states
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_invocation_pre_dispatch
            WHERE created_at < @cutoff_receipt
              AND pre_dispatch_state NOT IN ({protectedStates})
            """,
            ("cutoff_receipt", cutoffReceipt));

        // 2. Clean up terminal budget reservations (Released, Committed, Denied)
        //    but protect Reserved (active) reservations
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_budget_reservations
            WHERE created_at < @cutoff_budget
              AND state NOT IN ('{(int)AgentToolBudgetReservationState.Reserved}')
            """,
            ("cutoff_budget", cutoffBudget));

        // 3. Clean up terminal governance checkpoints (only for terminal invocations)
        //    The checkpoint itself has its own retention
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_pre_dispatch_checkpoints
            WHERE accepted_at < @cutoff_checkpoint
            """,
            ("cutoff_checkpoint", cutoffCheckpoint));

        // 4. Clean up terminal governance finalizations
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_governance_decisions
            WHERE recorded_at < @cutoff_finalization
            """,
            ("cutoff_finalization", cutoffFinalization));

        // 5. Clean up mutable reconciliation observations
        //    StillPending observations are protected (non-terminal)
        totalDeleted += await ExecuteNonQueryAsync(session,
            $"""
            DELETE FROM {_options.Schema}.agent_tool_reconciliation_observations
            WHERE observed_at < @cutoff_observation
              AND status != '{(int)AgentToolPreDispatchReconciliationStatus.StillPending}'
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