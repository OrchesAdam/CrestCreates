using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlWorkflowInstanceStore : IWorkflowInstanceStore
{
    private const string PrimaryKey = "runtime_workflow_instances_pkey";
    private const string WaitingKey = "ux_runtime_workflow_waiting";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;

    public PostgreSqlWorkflowInstanceStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _coordinator = coordinator;
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_workflow_instances");
    }

    public Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask(AddCoreAsync(instance, ct)), cancellationToken).AsTask();

    public Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask(UpdateCoreAsync(instance, expectedRevision, ct)), cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowInstance?>(ReadOneAsync(
            "where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id", command => PostgreSqlRuntimeStoreSupport.AddKey(command, key), ct)), cancellationToken).AsTask();

    public Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(RuntimeInstanceKey humanTaskKey, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowInstance?>(ReadOneAsync(
            "where tenant_scope_kind=@scope and tenant_id=@tenant and waiting_instance_id=@id", command => PostgreSqlRuntimeStoreSupport.AddKey(command, humanTaskKey), ct)), cancellationToken).AsTask();

    private async Task AddCoreAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        Validate(instance, expectedRevision: 0);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"insert into {_table} (tenant_scope_kind, tenant_id, instance_id, revision, status, workflow_pin_json, waiting_instance_id, suspension_operation_id, state_json, created_at, updated_at) values (@scope, @tenant, @id, 1, @status, @pin, @waiting, @operation, @state, @created, @updated);");
        AddWriteParameters(command, Persisted(instance, 1), operationId: null);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, PrimaryKey))
        {
            throw new RuntimeDuplicateEntityException(RuntimeDuplicateEntityCode.DuplicateInstance, "Workflow instance already exists.");
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, WaitingKey))
        {
            throw PostgreSqlRuntimeStoreSupport.Correlation("A Workflow is already waiting for this tenant-local HumanTask.");
        }
    }

    private async Task UpdateCoreAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken)
    {
        Validate(instance, expectedRevision);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"update {_table} set revision=@next, status=@status, workflow_pin_json=@pin, waiting_instance_id=@waiting, suspension_operation_id=@operation, state_json=@state, updated_at=@updated where tenant_scope_kind=@scope and tenant_id=@tenant and instance_id=@id and revision=@expected;");
        AddWriteParameters(command, Persisted(instance, expectedRevision + 1), operationId: null);
        command.Parameters.AddWithValue("next", expectedRevision + 1);
        command.Parameters.AddWithValue("expected", expectedRevision);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new RuntimeConcurrencyException("Workflow revision is stale or the instance is missing.");
        }
        catch (PostgresException exception) when (PostgreSqlRuntimeStoreSupport.IsUniqueViolation(exception, WaitingKey))
        {
            throw PostgreSqlRuntimeStoreSupport.Correlation("A Workflow is already waiting for this tenant-local HumanTask.");
        }
    }

    private async Task<WorkflowInstance?> ReadOneAsync(
        string predicate,
        Action<NpgsqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select revision, state_json::text from {_table} {predicate};");
        addParameters(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        var revision = reader.GetInt64(0);
        var result = PostgreSqlRuntimeStoreSupport.Deserialize(reader.GetString(1), PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowInstance);
        result.Key.EnsureValid();
        result.WorkflowPin.EnsureValid();
        if (result.Revision != revision)
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "Workflow state JSON revision does not match the durable revision column.");
        }
        return result.Snapshot();
    }

    private static void Validate(WorkflowInstance instance, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(instance);
        instance.Key.EnsureValid();
        instance.WorkflowPin.EnsureValid();
        if (instance.Revision != expectedRevision)
            throw new RuntimeConcurrencyException("Workflow candidate revision does not match the expected revision.");
        if (instance.WaitingHumanTaskKey is { } waiting && waiting.TenantId != instance.TenantId)
            throw PostgreSqlRuntimeStoreSupport.Correlation("Workflow waiting HumanTask must be in the same tenant scope.");
    }

    private static void AddWriteParameters(NpgsqlCommand command, WorkflowInstance instance, string? operationId)
    {
        PostgreSqlRuntimeStoreSupport.AddKey(command, instance.Key);
        command.Parameters.AddWithValue("status", (int)instance.Status);
        PostgreSqlRuntimeStoreSupport.AddJson(command, "pin", PostgreSqlRuntimeStoreSupport.Serialize(instance.WorkflowPin, PostgreSqlRuntimeJsonSerializerContext.Default.RuntimeDescriptorPin));
        command.Parameters.AddWithValue("waiting", (object?)instance.WaitingHumanTaskKey?.InstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("operation", (object?)operationId ?? DBNull.Value);
        PostgreSqlRuntimeStoreSupport.AddJson(command, "state", PostgreSqlRuntimeStoreSupport.Serialize(instance, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowInstance));
        command.Parameters.AddWithValue("created", instance.StartedAt);
        command.Parameters.AddWithValue("updated", instance.UpdatedAt ?? DateTimeOffset.UtcNow);
    }

    private static WorkflowInstance Persisted(WorkflowInstance instance, long revision)
    {
        var copy = instance.Snapshot();
        copy.Revision = revision;
        copy.UpdatedAt ??= DateTimeOffset.UtcNow;
        return copy;
    }
}
