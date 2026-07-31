using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private const string PrimaryKey = "runtime_human_task_instances_pkey";
    private const string ActiveStepKey = "ux_runtime_human_task_active_step";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;

    public PostgreSqlHumanTaskInstanceStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _coordinator = coordinator;
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_human_task_instances");
    }

    public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask(AddCoreAsync(instance, ct)), cancellationToken).AsTask();

    public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask(UpdateCoreAsync(instance, expectedRevision, ct)), cancellationToken).AsTask();

    public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<HumanTaskInstance?>(ReadOneAsync(
            "where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id", command => PostgreSqlRuntimeStoreSupport.AddKey(command, key), ct)), cancellationToken).AsTask();

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default)
        => QueryPendingAsync("where tenant_scope_kind=@scope and tenant_id=@tenant and workflow_instance_id=@id", workflowKey, cancellationToken);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default)
        => QueryPendingAsync("where tenant_scope_kind=@scope and tenant_id=@tenant and assignee_user_id=@id", new RuntimeInstanceKey(scope.TenantId, assigneeUserId), cancellationToken);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default)
        => QueryPendingAndFilterAsync(scope, item => item.CandidateUserIds.Contains(userId, StringComparer.Ordinal), cancellationToken);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default)
        => QueryPendingAndFilterAsync(scope, item => item.CandidateRoleIds.Contains(roleId, StringComparer.Ordinal), cancellationToken);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default)
        => QueryPendingAndFilterAsync(scope, item => string.Equals(item.OrganizationUnitId, organizationUnitId, StringComparison.Ordinal), cancellationToken);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default)
        => QueryPendingAndFilterAsync(scope, item => string.Equals(item.PositionId, positionId, StringComparison.Ordinal), cancellationToken);

    private async Task AddCoreAsync(HumanTaskInstance instance, CancellationToken cancellationToken)
    {
        Validate(instance, 0);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"insert into {_table} (tenant_scope_kind, tenant_id, instance_id, revision, status, human_task_pin_json, workflow_instance_id, workflow_step_id, suspension_operation_id, assignee_user_id, state_json, created_at, updated_at) values (@scope, @tenant, @id, 1, @status, @pin, @workflow, @step, @operation, @assignee, @state, @created, @updated);");
        AddWriteParameters(command, Persisted(instance, 1), null);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, PrimaryKey))
        {
            throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "HumanTask instance already exists.");
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, ActiveStepKey))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.ActiveStepCorrelationConflict,
                "A HumanTask already exists for the Workflow step correlation.");
        }
    }

    private async Task UpdateCoreAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken)
    {
        Validate(instance, expectedRevision);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"update {_table} set revision=@next, status=@status, human_task_pin_json=@pin, workflow_instance_id=@workflow, workflow_step_id=@step, suspension_operation_id=@operation, assignee_user_id=@assignee, state_json=@state, updated_at=@updated where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id and revision=@expected;");
        AddWriteParameters(command, Persisted(instance, expectedRevision + 1), null);
        command.Parameters.AddWithValue("next", expectedRevision + 1);
        command.Parameters.AddWithValue("expected", expectedRevision);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RuntimeConcurrencyException("HumanTask revision is stale or the instance is missing.");
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, ActiveStepKey))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.ActiveStepCorrelationConflict,
                "A HumanTask already exists for the Workflow step correlation.");
        }
    }

    private Task<IReadOnlyList<HumanTaskInstance>> QueryPendingAsync(string predicate, RuntimeInstanceKey key, CancellationToken cancellationToken)
        => _coordinator.ExecuteAsync(ct => new ValueTask<IReadOnlyList<HumanTaskInstance>>(ReadManyAsync(
            $"{predicate} and status in (@created, @assigned) order by created_at, instance_id collate \"C\"", command =>
            {
                PostgreSqlRuntimeStoreSupport.AddKey(command, key);
                command.Parameters.AddWithValue("created", (int)HumanTaskInstanceStatus.Created);
                command.Parameters.AddWithValue("assigned", (int)HumanTaskInstanceStatus.Assigned);
            }, ct)), cancellationToken).AsTask();

    private Task<IReadOnlyList<HumanTaskInstance>> QueryPendingAndFilterAsync(
        RuntimeTenantScope scope,
        Func<HumanTaskInstance, bool> predicate,
        CancellationToken cancellationToken)
        => _coordinator.ExecuteAsync(async ct =>
        {
            scope.EnsureValid();
            var items = await ReadManyAsync(
                "where tenant_scope_kind=@scope and tenant_id=@tenant and status in (@created, @assigned) order by created_at, instance_id collate \"C\"",
                command =>
                {
                    PostgreSqlRuntimeStoreSupport.AddScope(command, scope);
                    command.Parameters.AddWithValue("created", (int)HumanTaskInstanceStatus.Created);
                    command.Parameters.AddWithValue("assigned", (int)HumanTaskInstanceStatus.Assigned);
                }, ct).ConfigureAwait(false);
            return (IReadOnlyList<HumanTaskInstance>)items.Where(predicate).ToArray();
        }, cancellationToken).AsTask();

    private async Task<HumanTaskInstance?> ReadOneAsync(string predicate, Action<NpgsqlCommand> addParameters, CancellationToken cancellationToken)
    {
        var values = await ReadManyAsync(predicate, addParameters, cancellationToken).ConfigureAwait(false);
        return values.SingleOrDefault();
    }

    private async Task<IReadOnlyList<HumanTaskInstance>> ReadManyAsync(string predicate, Action<NpgsqlCommand> addParameters, CancellationToken cancellationToken)
    {
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select revision, state_json::text from {_table} {predicate};");
        addParameters(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new List<HumanTaskInstance>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var revision = reader.GetInt64(0);
            var value = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(1), PostgreSqlRuntimeJsonSerializerContext.Default.HumanTaskInstance);
            value.Key.EnsureValid();
            value.HumanTaskPin.EnsureValid();
            if (value.Revision != revision)
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    "HumanTask state JSON revision does not match the durable revision column.");
            }
            values.Add(value.Snapshot());
        }
        return values;
    }

    private static void Validate(HumanTaskInstance instance, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(instance);
        instance.Key.EnsureValid();
        instance.HumanTaskPin.EnsureValid();
        if (instance.Revision != expectedRevision)
            throw new RuntimeConcurrencyException("HumanTask candidate revision does not match the expected revision.");
        if (instance.WorkflowKey is { } workflow && workflow.TenantId != instance.TenantId)
            throw PostgreSqlRuntimeStoreSupport.Correlation("HumanTask Workflow correlation must be tenant-local.");
    }

    private static void AddWriteParameters(NpgsqlCommand command, HumanTaskInstance instance, string? operationId)
    {
        PostgreSqlRuntimeStoreSupport.AddKey(command, instance.Key);
        command.Parameters.AddWithValue("status", (int)instance.Status);
        PostgreSqlRuntimeStoreSupport.AddJson(command, "pin", PostgreSqlRuntimeStoreSupport.Serialize(instance.HumanTaskPin, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeDescriptorPin));
        command.Parameters.AddWithValue("workflow", (object?)instance.WorkflowKey?.InstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("step", (object?)instance.WorkflowStepId ?? DBNull.Value);
        command.Parameters.AddWithValue("operation", (object?)operationId ?? DBNull.Value);
        command.Parameters.AddWithValue("assignee", (object?)instance.AssigneeUserId ?? DBNull.Value);
        PostgreSqlRuntimeStoreSupport.AddJson(command, "state", PostgreSqlRuntimeStoreSupport.Serialize(instance, PostgreSqlRuntimeJsonSerializerContext.Default.HumanTaskInstance));
        command.Parameters.AddWithValue("created", instance.CreatedAt);
        command.Parameters.AddWithValue("updated", instance.UpdatedAt ?? DateTimeOffset.UtcNow);
    }

    private static HumanTaskInstance Persisted(HumanTaskInstance instance, long revision)
    {
        var copy = instance.Snapshot();
        copy.Revision = revision;
        copy.UpdatedAt ??= DateTimeOffset.UtcNow;
        return copy;
    }
}
