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
    public PostgreSqlWorkflowContinuationAcceptanceStore(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator)
    { _options = options; _coordinator = coordinator; _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_workflow_continuation_acceptances"); }

    public async Task<WorkflowContinuationAcceptanceWriteResult> AddAsync(WorkflowContinuationAcceptance acceptance, CancellationToken cancellationToken = default)
    {
        var session = _coordinator.RequireSession(); using var lease = session.EnterCommand();
        var computed = WorkflowContinuationAcceptanceCanonicalWriter.Compute(acceptance);
        if (!string.Equals(computed.Value, acceptance.Integrity.Value, StringComparison.Ordinal)) return WorkflowContinuationAcceptanceWriteResult.Conflict;
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"insert into {_table} (tenant_scope_kind, tenant_id, completion_event_id, human_task_instance_id, workflow_instance_id, outcome, result_json, workflow_from_revision, workflow_to_revision, integrity_json, accepted_at) values (@scope, @tenant, @event, @task, @workflow, @outcome, @result, @from, @to, @integrity, @accepted);");
        PostgreSqlRuntimeStoreSupport.AddScope(command, acceptance.TenantScope); command.Parameters.AddWithValue("event", acceptance.CompletionEventId); command.Parameters.AddWithValue("task", acceptance.HumanTaskKey.InstanceId); command.Parameters.AddWithValue("workflow", acceptance.WorkflowKey.InstanceId); command.Parameters.AddWithValue("outcome", acceptance.Outcome); PostgreSqlRuntimeStoreSupport.AddJson(command, "result", acceptance.Result is null ? "null" : PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Result, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue)); command.Parameters.AddWithValue("from", acceptance.WorkflowFromRevision); command.Parameters.AddWithValue("to", acceptance.WorkflowToRevision); PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(acceptance.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash)); command.Parameters.AddWithValue("accepted", acceptance.AcceptedAt);
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); return WorkflowContinuationAcceptanceWriteResult.Accepted; }
        catch (PostgresException ex) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(ex, "runtime_workflow_continuation_acceptances_pkey")) { return WorkflowContinuationAcceptanceWriteResult.Duplicate; }
        catch (PostgresException ex) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(ex, "uq_runtime_continuation_acceptance_task")) { return WorkflowContinuationAcceptanceWriteResult.Conflict; }
    }

    public async Task<WorkflowContinuationAcceptance?> GetAsync(RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken = default)
    {
        var session = _coordinator.RequireSession(); using var lease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options, $"select human_task_instance_id, workflow_instance_id, outcome, result_json::text, workflow_from_revision, workflow_to_revision, integrity_json::text, accepted_at from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and completion_event_id=@event;");
        PostgreSqlRuntimeStoreSupport.AddScope(command, scope); command.Parameters.AddWithValue("event", completionEventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var resultJson = reader.IsDBNull(3) ? null : reader.GetString(3); var integrity = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(6), PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash);
        return new WorkflowContinuationAcceptance { TenantScope = scope, CompletionEventId = completionEventId, HumanTaskKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(0)), WorkflowKey = new RuntimeInstanceKey(scope.TenantId, reader.GetString(1)), Outcome = reader.GetString(2), Result = resultJson is null or "null" ? null : PostgreSqlRuntimeStoreSupport.Deserialize(resultJson, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeStateValue), WorkflowFromRevision = reader.GetInt64(4), WorkflowToRevision = reader.GetInt64(5), Integrity = integrity, AcceptedAt = reader.GetFieldValue<DateTimeOffset>(7) };
    }
}
