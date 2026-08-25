using System.Text.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;
using Npgsql;
using CrestCreates.Workflow.Abstractions.Delivery;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlWorkflowContinuationAcceptanceStore : IWorkflowContinuationAcceptanceStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;
    private readonly NpgsqlDataSource _dataSource;
    public PostgreSqlWorkflowContinuationAcceptanceStore(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator, NpgsqlDataSource dataSource)
    { _options = options; _coordinator = coordinator; _dataSource = dataSource; _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_workflow_continuation_acceptances"); }

    public async Task<WorkflowContinuationAcceptanceWriteResult> AddAsync(WorkflowContinuationAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        var session = _coordinator.RequireSession();
        // PostgreSQL timestamptz has microsecond precision. Normalize the
        // receipt before hashing/serialization so the scalar accepted_at and
        // the generated receipt remain byte-for-byte consistent on readback.
        acceptance = acceptance with { AcceptedAt = TruncateToPostgreSqlPrecision(acceptance.AcceptedAt) };
        var computed = WorkflowContinuationAcceptanceCanonicalWriter.Compute(acceptance);
        if (!string.Equals(computed.Value, acceptance.Integrity.Value, StringComparison.Ordinal)) return WorkflowContinuationAcceptanceWriteResult.Conflict;
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"insert into {_table} (tenant_scope_kind, tenant_id, completion_event_id, human_task_instance_id, workflow_instance_id, outcome, result_json, workflow_from_revision, workflow_to_revision, integrity_json, receipt_json, accepted_at) values (@scope, @tenant, @event, @task, @workflow, @outcome, @result, @from, @to, @integrity, @receipt, @accepted) on conflict do nothing;");
        PostgreSqlRuntimeStoreSupport.AddScope(command, acceptance.TenantScope); command.Parameters.AddWithValue("event", acceptance.CompletionEventId); command.Parameters.AddWithValue("task", acceptance.HumanTaskKey.InstanceId); command.Parameters.AddWithValue("workflow", acceptance.WorkflowKey.InstanceId); command.Parameters.AddWithValue("outcome", acceptance.Outcome); PostgreSqlRuntimeStoreSupport.AddJson(command, "result", acceptance.Result is null ? "null" : PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Result, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue)); command.Parameters.AddWithValue("from", acceptance.WorkflowFromRevision); command.Parameters.AddWithValue("to", acceptance.WorkflowToRevision); PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash)); PostgreSqlRuntimeStoreSupport.AddJson(command, "receipt", PostgreSqlRuntimeStoreSupport.Serialize(acceptance, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowContinuationAcceptance)); command.Parameters.AddWithValue("accepted", acceptance.AcceptedAt);
        int inserted;
        using (session.EnterCommand())
            inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 1) return WorkflowContinuationAcceptanceWriteResult.Accepted;

        var existingAcceptance = await GetAsync(acceptance.TenantScope, acceptance.CompletionEventId, cancellationToken).ConfigureAwait(false);
        if (existingAcceptance is not null)
        {
            var persistedIntegrity = WorkflowContinuationAcceptanceCanonicalWriter.Compute(existingAcceptance);
            var same = existingAcceptance.HumanTaskKey == acceptance.HumanTaskKey
                && existingAcceptance.WorkflowKey == acceptance.WorkflowKey
                && string.Equals(existingAcceptance.Outcome, acceptance.Outcome, StringComparison.Ordinal)
                && string.Equals(persistedIntegrity.Value, acceptance.Integrity.Value, StringComparison.Ordinal)
                && string.Equals(existingAcceptance.Integrity.Value, persistedIntegrity.Value, StringComparison.Ordinal);
            return same ? WorkflowContinuationAcceptanceWriteResult.Duplicate : WorkflowContinuationAcceptanceWriteResult.Conflict;
        }
        // Absence of both durable proofs is not evidence of an exact replay.
        // Fail closed so callers cannot acknowledge a completion without a
        // receipt or the conflicting waiting correlation.
        return WorkflowContinuationAcceptanceWriteResult.Conflict;
    }

    private static DateTimeOffset TruncateToPostgreSqlPrecision(DateTimeOffset value)
        => value.AddTicks(-(value.Ticks % 10));

    public async Task<WorkflowContinuationAcceptance?> GetAsync(RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken = default)
    {
        if (_coordinator.TryGetSession(out var ambient))
        {
            using var lease = ambient!.EnterCommand();
            return await ReadAsync(ambient.Connection, ambient.Transaction, scope, completionEventId, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, transaction: null, scope, completionEventId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkflowContinuationAcceptance?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"select human_task_instance_id, workflow_instance_id, outcome, result_json::text, workflow_from_revision, workflow_to_revision, integrity_json::text, receipt_json::text, accepted_at from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and completion_event_id=@event;", connection, transaction);
        PostgreSqlRuntimeStoreSupport.AddScope(command, scope); command.Parameters.AddWithValue("event", completionEventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var resultJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        var integrity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(6), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
        var acceptedAt = reader.GetFieldValue<DateTimeOffset>(8);
        var scalar = new WorkflowContinuationAcceptance
        {
            TenantScope = scope,
            CompletionEventId = completionEventId,
            HumanTaskKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(0)),
            WorkflowKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(1)),
            Outcome = reader.GetString(2),
            Result = resultJson is null or "null" ? null : PostgreSqlRuntimeStoreSupport.Deserialize(resultJson, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue),
            WorkflowFromRevision = reader.GetInt64(4),
            WorkflowToRevision = reader.GetInt64(5),
            Integrity = integrity,
            AcceptedAt = acceptedAt
        };
        var receipt = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(7), PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowContinuationAcceptance);
        var recomputed = WorkflowContinuationAcceptanceCanonicalWriter.Compute(scalar);
        if (!ReceiptMatches(receipt, scalar) || !string.Equals(recomputed.Value, integrity.Value, StringComparison.Ordinal))
            throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Workflow continuation acceptance receipt or integrity does not match its scalar columns.");
        return receipt;
    }

    private static bool ReceiptMatches(WorkflowContinuationAcceptance receipt, WorkflowContinuationAcceptance scalar)
        => receipt.TenantScope == scalar.TenantScope
            && string.Equals(receipt.CompletionEventId, scalar.CompletionEventId, StringComparison.Ordinal)
            && receipt.HumanTaskKey == scalar.HumanTaskKey
            && receipt.WorkflowKey == scalar.WorkflowKey
            && string.Equals(receipt.Outcome, scalar.Outcome, StringComparison.Ordinal)
            && ResultsEqual(receipt.Result, scalar.Result)
            && receipt.WorkflowFromRevision == scalar.WorkflowFromRevision
            && receipt.WorkflowToRevision == scalar.WorkflowToRevision
            && receipt.Integrity == scalar.Integrity
            && receipt.AcceptedAt == scalar.AcceptedAt;

    private static bool ResultsEqual(RuntimeStateValue? left, RuntimeStateValue? right)
        => left is null && right is null
            || left is not null && right is not null
                && string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal)
                && string.Equals(left.SchemaRef?.ToString(), right.SchemaRef?.ToString(), StringComparison.Ordinal)
                && string.Equals(left.JsonPayload, right.JsonPayload, StringComparison.Ordinal);
}
