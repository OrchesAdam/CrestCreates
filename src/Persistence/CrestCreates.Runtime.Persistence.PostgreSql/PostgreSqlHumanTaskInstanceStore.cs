using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;
    public PostgreSqlHumanTaskInstanceStore(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator)
    { _coordinator = coordinator; _table = $"\"{options.Schema.Replace("\"", "\"\"")}\".runtime_human_task_instances"; }
    public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(async ct =>
    {
        if (instance.Revision != 0) throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Revision must be zero on Add.");
        var s = _coordinator.RequireSession();
        await using var c = new NpgsqlCommand($"insert into {_table} (tenant_scope_kind, tenant_id, instance_id, revision, status, human_task_namespace, human_task_id, human_task_version, contract_hash, definition_hash, workflow_scope_kind, workflow_tenant_id, workflow_instance_id, outcome) values (@scope,@tenant,@id,1,@status,@ns,@ht,@ver,@contract,@definition,@wscope,@wtenant,@wid,@outcome)", s.Connection, s.Transaction);
        c.Parameters.AddWithValue("scope", Scope(instance.TenantId)); c.Parameters.AddWithValue("tenant", StoredTenant(instance.TenantId)); c.Parameters.AddWithValue("id", instance.Id); c.Parameters.AddWithValue("status", (int)instance.Status); c.Parameters.AddWithValue("ns", instance.HumanTaskPin.Ref.Namespace); c.Parameters.AddWithValue("ht", instance.HumanTaskPin.Ref.Id); c.Parameters.AddWithValue("ver", instance.HumanTaskPin.Ref.Version ?? 0); c.Parameters.AddWithValue("contract", instance.HumanTaskPin.ContractHash.Value); c.Parameters.AddWithValue("definition", instance.HumanTaskPin.DefinitionHash.Value); c.Parameters.AddWithValue("wscope", (object?)instance.WorkflowKey is null ? DBNull.Value : Scope(instance.WorkflowKey.Value.TenantId)); c.Parameters.AddWithValue("wtenant", (object?)instance.WorkflowKey is null ? DBNull.Value : StoredTenant(instance.WorkflowKey.Value.TenantId)); c.Parameters.AddWithValue("wid", (object?)instance.WorkflowKey?.InstanceId ?? DBNull.Value); c.Parameters.AddWithValue("outcome", (object?)instance.Outcome ?? DBNull.Value);
        try { await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false); } catch (PostgresException ex) when (ex.SqlState == "23505") { throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Human task instance already exists."); }
    }, cancellationToken).AsTask();
    public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(async ct => { var s = _coordinator.RequireSession(); await using var c = new NpgsqlCommand($"update {_table} set revision=@next,status=@status,outcome=@outcome where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id and revision=@expected", s.Connection, s.Transaction); c.Parameters.AddWithValue("next", expectedRevision + 1); c.Parameters.AddWithValue("expected", expectedRevision); c.Parameters.AddWithValue("scope", Scope(instance.TenantId)); c.Parameters.AddWithValue("tenant", StoredTenant(instance.TenantId)); c.Parameters.AddWithValue("id", instance.Id); c.Parameters.AddWithValue("status", (int)instance.Status); c.Parameters.AddWithValue("outcome", (object?)instance.Outcome ?? DBNull.Value); if (await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new RuntimeConcurrencyException("Human task revision is stale or missing."); }, cancellationToken).AsTask();
    public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => _coordinator.ExecuteAsync(ct => new ValueTask<HumanTaskInstance?>(ReadAsync("where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id", [new("scope", Scope(key.TenantId)), new("tenant", StoredTenant(key.TenantId)), new("id", key.InstanceId)], ct)), cancellationToken).AsTask();
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => QueryAsync("where workflow_scope_kind=@scope and workflow_tenant_id=@tenant and workflow_instance_id=@id and status in (0,1)", workflowKey, cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => QueryAsync("where tenant_scope_kind=@scope and tenant_id=@tenant and assignee_user_id=@id and status in (0,1)", new RuntimeInstanceKey(scope.TenantId, assigneeUserId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => QueryAsync("where false", new RuntimeInstanceKey(scope.TenantId, userId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => QueryAsync("where false", new RuntimeInstanceKey(scope.TenantId, roleId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => QueryAsync("where false", new RuntimeInstanceKey(scope.TenantId, organizationUnitId), cancellationToken);
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => QueryAsync("where false", new RuntimeInstanceKey(scope.TenantId, positionId), cancellationToken);
    private async Task<IReadOnlyList<HumanTaskInstance>> QueryAsync(string predicate, RuntimeInstanceKey key, CancellationToken ct)
    {
        var s = _coordinator.RequireSession();
        await using var c = new NpgsqlCommand($"select tenant_scope_kind,tenant_id,instance_id,revision,status,human_task_namespace,human_task_id,human_task_version,contract_hash,definition_hash,workflow_scope_kind,workflow_tenant_id,workflow_instance_id,outcome from {_table} {predicate}", s.Connection, s.Transaction);
        c.Parameters.AddWithValue("scope", Scope(key.TenantId));
        c.Parameters.AddWithValue("tenant", StoredTenant(key.TenantId));
        c.Parameters.AddWithValue("id", key.InstanceId);
        await using var r = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<HumanTaskInstance>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(Map(r));
        return result;
    }

    private async Task<HumanTaskInstance?> ReadAsync(string predicate, NpgsqlParameter[] parameters, CancellationToken ct)
    {
        var s = _coordinator.RequireSession();
        await using var c = new NpgsqlCommand($"select tenant_scope_kind,tenant_id,instance_id,revision,status,human_task_namespace,human_task_id,human_task_version,contract_hash,definition_hash,workflow_scope_kind,workflow_tenant_id,workflow_instance_id,outcome from {_table} {predicate}", s.Connection, s.Transaction);
        c.Parameters.AddRange(parameters);
        await using var r = await c.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    private static HumanTaskInstance Map(NpgsqlDataReader r) => new()
    {
        Key = new RuntimeInstanceKey(r.GetString(0) == "host" ? null : r.GetString(1), r.GetString(2)),
        Revision = r.GetInt64(3),
        Status = (HumanTaskInstanceStatus)r.GetInt32(4),
        HumanTaskPin = new Metadata.Abstractions.Runtime.RuntimeDescriptorPin
        {
            Ref = new Metadata.Abstractions.DescriptorRef(r.GetString(5), r.GetString(6), r.GetInt32(7)),
            ContractHash = Placeholder(r.GetString(8), "Contract", "HumanTask"),
            DefinitionHash = Placeholder(r.GetString(9), "Definition", "HumanTask")
        },
        WorkflowKey = r.IsDBNull(12) ? null : new RuntimeInstanceKey(r.GetString(10) == "host" ? null : r.GetString(11), r.GetString(12)),
        Outcome = r.IsDBNull(13) ? null : r.GetString(13)
    };
    private static Metadata.Abstractions.CanonicalHashing.CanonicalHash Placeholder(string value, string purpose, string kind) => new() { Value = value, Algorithm = "persisted", AlgorithmVersion = "1", ArtifactKind = "Descriptor", DescriptorKind = kind, Scope = "InternalFull", Purpose = purpose, ContractVersion = "1", CanonicalShapeVersion = "1" };
    private static string Scope(string? tenantId) => tenantId is null ? "host" : "tenant";
    private static string StoredTenant(string? tenantId) => tenantId ?? string.Empty;
}
