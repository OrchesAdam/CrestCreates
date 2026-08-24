using System.Text.Json;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
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
        var computed = WorkflowContinuationAcceptanceCanonicalWriter.Compute(acceptance);
        if (!string.Equals(computed.Value, acceptance.Integrity.Value, StringComparison.Ordinal)) return WorkflowContinuationAcceptanceWriteResult.Conflict;
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"insert into {_table} (tenant_scope_kind, tenant_id, completion_event_id, human_task_instance_id, workflow_instance_id, outcome, result_json, workflow_from_revision, workflow_to_revision, integrity_json, accepted_at) values (@scope, @tenant, @event, @task, @workflow, @outcome, @result, @from, @to, @integrity, @accepted) on conflict do nothing;");
        PostgreSqlRuntimeStoreSupport.AddScope(command, acceptance.TenantScope); command.Parameters.AddWithValue("event", acceptance.CompletionEventId); command.Parameters.AddWithValue("task", acceptance.HumanTaskKey.InstanceId); command.Parameters.AddWithValue("workflow", acceptance.WorkflowKey.InstanceId); command.Parameters.AddWithValue("outcome", acceptance.Outcome); PostgreSqlRuntimeStoreSupport.AddJson(command, "result", acceptance.Result is null ? "null" : PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Result, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue)); command.Parameters.AddWithValue("from", acceptance.WorkflowFromRevision); command.Parameters.AddWithValue("to", acceptance.WorkflowToRevision); PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash)); command.Parameters.AddWithValue("accepted", acceptance.AcceptedAt);
        int inserted;
        using (session.EnterCommand())
            inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 1) return WorkflowContinuationAcceptanceWriteResult.Accepted;

        await using var existing = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select human_task_instance_id, workflow_instance_id, integrity_json::text from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and completion_event_id=@event;");
        PostgreSqlRuntimeStoreSupport.AddScope(existing, acceptance.TenantScope);
        existing.Parameters.AddWithValue("event", acceptance.CompletionEventId);
        using (session.EnterCommand())
        {
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var same = string.Equals(reader.GetString(0), acceptance.HumanTaskKey.InstanceId, StringComparison.Ordinal)
                    && string.Equals(reader.GetString(1), acceptance.WorkflowKey.InstanceId, StringComparison.Ordinal)
                    && string.Equals(PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(2), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash).Value, acceptance.Integrity.Value, StringComparison.Ordinal);
                return same ? WorkflowContinuationAcceptanceWriteResult.Duplicate : WorkflowContinuationAcceptanceWriteResult.Conflict;
            }
        }
        await using var byTask = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select completion_event_id from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and human_task_instance_id=@task;");
        PostgreSqlRuntimeStoreSupport.AddScope(byTask, acceptance.TenantScope);
        byTask.Parameters.AddWithValue("task", acceptance.HumanTaskKey.InstanceId);
        object? otherEvent;
        using (session.EnterCommand())
            otherEvent = await byTask.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return otherEvent is not null ? WorkflowContinuationAcceptanceWriteResult.Conflict : WorkflowContinuationAcceptanceWriteResult.Duplicate;
    }

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
        await using var command = new NpgsqlCommand($"select human_task_instance_id, workflow_instance_id, outcome, result_json::text, workflow_from_revision, workflow_to_revision, integrity_json::text, accepted_at from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and completion_event_id=@event;", connection, transaction);
        PostgreSqlRuntimeStoreSupport.AddScope(command, scope); command.Parameters.AddWithValue("event", completionEventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var resultJson = reader.IsDBNull(3) ? null : reader.GetString(3); var integrity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(6), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
        return new WorkflowContinuationAcceptance { TenantScope = scope, CompletionEventId = completionEventId, HumanTaskKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(0)), WorkflowKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(1)), Outcome = reader.GetString(2), Result = resultJson is null or "null" ? null : PostgreSqlRuntimeStoreSupport.Deserialize(resultJson, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue), WorkflowFromRevision = reader.GetInt64(4), WorkflowToRevision = reader.GetInt64(5), Integrity = integrity, AcceptedAt = reader.GetFieldValue<DateTimeOffset>(7) };
    }
}
