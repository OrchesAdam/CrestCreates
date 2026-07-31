using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;
    public PostgreSqlWorkflowInstanceStore(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator)
    { _coordinator = coordinator; _table = $"\"{options.Schema.Replace("\"", "\"\"")}\".runtime_workflow_instances"; }

    public Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(async ct =>
    {
        if (instance.Revision != 0) throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Revision must be zero on Add.");
        var s = _coordinator.RequireSession();
        await using var command = new NpgsqlCommand($"insert into {_table} (tenant_scope_kind, tenant_id, instance_id, revision, status, workflow_namespace, workflow_id, workflow_version, contract_hash, definition_hash, waiting_scope_kind, waiting_tenant_id, waiting_instance_id) values (@scope,@tenant,@id,1,@status,@ns,@wf,@ver,@contract,@definition,@wscope,@wtenant,@wid)", s.Connection, s.Transaction);
        AddParameters(command, instance);
        try { await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        catch (PostgresException ex) when (ex.SqlState == "23505") { throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Workflow instance already exists."); }
    }, cancellationToken).AsTask();

    public Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(async ct =>
    {
        var s = _coordinator.RequireSession();
        await using var command = new NpgsqlCommand($"update {_table} set revision=@next, status=@status, waiting_scope_kind=@wscope, waiting_tenant_id=@wtenant, waiting_instance_id=@wid, updated_at=clock_timestamp() where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id and revision=@expected", s.Connection, s.Transaction);
        command.Parameters.AddWithValue("next", expectedRevision + 1); command.Parameters.AddWithValue("expected", expectedRevision);
        command.Parameters.AddWithValue("status", (int)instance.Status); command.Parameters.AddWithValue("scope", Scope(instance.TenantId)); command.Parameters.AddWithValue("tenant", StoredTenant(instance.TenantId)); command.Parameters.AddWithValue("id", instance.InstanceId);
        command.Parameters.AddWithValue("wscope", (object?)instance.WaitingHumanTaskKey is null ? DBNull.Value : Scope(instance.WaitingHumanTaskKey.Value.TenantId)); command.Parameters.AddWithValue("wtenant", (object?)instance.WaitingHumanTaskKey is null ? DBNull.Value : StoredTenant(instance.WaitingHumanTaskKey.Value.TenantId)); command.Parameters.AddWithValue("wid", (object?)instance.WaitingHumanTaskKey?.InstanceId ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new RuntimeConcurrencyException("Workflow revision is stale or the instance is missing.");
    }, cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowInstance?>(ReadAsync("where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id", [new("scope", Scope(key.TenantId)), new("tenant", StoredTenant(key.TenantId)), new("id", key.InstanceId)], ct)), cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(RuntimeInstanceKey humanTaskKey, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowInstance?>(ReadAsync("where waiting_scope_kind=@scope and waiting_tenant_id=@tenant and waiting_instance_id=@id", [new("scope", Scope(humanTaskKey.TenantId)), new("tenant", StoredTenant(humanTaskKey.TenantId)), new("id", humanTaskKey.InstanceId)], ct)), cancellationToken).AsTask();

    private async Task<WorkflowInstance?> ReadAsync(string predicate, NpgsqlParameter[] parameters, CancellationToken ct)
    {
        var s = _coordinator.RequireSession();
        await using var command = new NpgsqlCommand($"select tenant_scope_kind, tenant_id, instance_id, revision, status, workflow_namespace, workflow_id, workflow_version, contract_hash, definition_hash, waiting_scope_kind, waiting_tenant_id, waiting_instance_id from {_table} {predicate}", s.Connection, s.Transaction);
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new WorkflowInstance
        {
            Key = new RuntimeInstanceKey(reader.GetString(0) == "host" ? null : reader.GetString(1), reader.GetString(2)),
            Revision = reader.GetInt64(3),
            Status = (WorkflowInstanceStatus)reader.GetInt32(4),
            WaitingHumanTaskKey = reader.IsDBNull(12) ? null : new RuntimeInstanceKey(reader.GetString(10) == "host" ? null : reader.GetString(11), reader.GetString(12)),
            WorkflowPin = new Metadata.Abstractions.Runtime.RuntimeDescriptorPin
            {
                Ref = new Metadata.Abstractions.DescriptorRef(reader.GetString(5), reader.GetString(6), reader.GetInt32(7)),
                ContractHash = new Metadata.Abstractions.CanonicalHashing.CanonicalHash { Value = reader.GetString(8), Algorithm = "persisted", AlgorithmVersion = "1", ArtifactKind = "Descriptor", DescriptorKind = "Workflow", Scope = "InternalFull", Purpose = "Contract", ContractVersion = "1", CanonicalShapeVersion = "1" },
                DefinitionHash = new Metadata.Abstractions.CanonicalHashing.CanonicalHash { Value = reader.GetString(9), Algorithm = "persisted", AlgorithmVersion = "1", ArtifactKind = "Descriptor", DescriptorKind = "Workflow", Scope = "InternalFull", Purpose = "Definition", ContractVersion = "1", CanonicalShapeVersion = "1" }
            }
        };
    }
    private static void AddParameters(NpgsqlCommand c, WorkflowInstance i)
    { c.Parameters.AddWithValue("scope", Scope(i.TenantId)); c.Parameters.AddWithValue("tenant", StoredTenant(i.TenantId)); c.Parameters.AddWithValue("id", i.InstanceId); c.Parameters.AddWithValue("status", (int)i.Status); c.Parameters.AddWithValue("ns", i.WorkflowPin.Ref.Namespace); c.Parameters.AddWithValue("wf", i.WorkflowPin.Ref.Id); c.Parameters.AddWithValue("ver", i.WorkflowPin.Ref.Version ?? 0); c.Parameters.AddWithValue("contract", i.WorkflowPin.ContractHash.Value); c.Parameters.AddWithValue("definition", i.WorkflowPin.DefinitionHash.Value); c.Parameters.AddWithValue("wscope", (object?)i.WaitingHumanTaskKey is null ? DBNull.Value : Scope(i.WaitingHumanTaskKey.Value.TenantId)); c.Parameters.AddWithValue("wtenant", (object?)i.WaitingHumanTaskKey is null ? DBNull.Value : StoredTenant(i.WaitingHumanTaskKey.Value.TenantId)); c.Parameters.AddWithValue("wid", (object?)i.WaitingHumanTaskKey?.InstanceId ?? DBNull.Value); }
    private static string Scope(string? tenantId) => tenantId is null ? "host" : "tenant";
    private static string StoredTenant(string? tenantId) => tenantId ?? string.Empty;
}
