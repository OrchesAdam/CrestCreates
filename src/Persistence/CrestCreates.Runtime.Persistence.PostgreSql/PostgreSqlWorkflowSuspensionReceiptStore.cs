using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

internal sealed class PostgreSqlWorkflowSuspensionReceiptStore : IWorkflowSuspensionReceiptStore
{
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;

    public PostgreSqlWorkflowSuspensionReceiptStore(
        PostgreSqlRuntimePersistenceOptions options,
        PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _coordinator = coordinator;
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_operation_receipts");
    }

    public Task<WorkflowSuspensionReceiptWriteResult> AddAsync(WorkflowSuspensionReceipt receipt, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowSuspensionReceiptWriteResult>(AddCoreAsync(receipt, ct)), cancellationToken).AsTask();

    public Task<WorkflowSuspensionReceipt?> GetAsync(RuntimeTenantScope scope, string suspensionOperationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowSuspensionReceipt?>(GetCoreAsync(scope, suspensionOperationId, ct)), cancellationToken).AsTask();

    private async Task<WorkflowSuspensionReceiptWriteResult> AddCoreAsync(WorkflowSuspensionReceipt receipt, CancellationToken cancellationToken)
    {
        Validate(receipt);
        string? accepted;
        {
            var session = _coordinator.RequireSession();
            using var commandLease = session.EnterCommand();
            await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
                $"insert into {_table} (tenant_scope_kind, tenant_id, operation_id, workflow_instance_id, human_task_instance_id, workflow_from_revision, workflow_to_revision, integrity_json, receipt_json) values (@scope, @tenant, @operation, @workflow, @task, @from, @to, @integrity, @receipt) on conflict (tenant_scope_kind, tenant_id, operation_id) do nothing returning receipt_json::text;");
            PostgreSqlRuntimeStoreSupport.AddScope(command, receipt.Scope);
            command.Parameters.AddWithValue("operation", receipt.SuspensionOperationId);
            command.Parameters.AddWithValue("workflow", receipt.WorkflowKey.InstanceId);
            command.Parameters.AddWithValue("task", receipt.HumanTaskKey.InstanceId);
            command.Parameters.AddWithValue("from", receipt.WorkflowFromRevision);
            command.Parameters.AddWithValue("to", receipt.WorkflowToRevision);
            PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(receipt.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
            PostgreSqlRuntimeStoreSupport.AddJson(command, "receipt", PostgreSqlRuntimeStoreSupport.Serialize(receipt, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowSuspensionReceipt));
            accepted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }
        if (accepted is string)
        {
            return new WorkflowSuspensionReceiptWriteResult
            {
                Status = WorkflowSuspensionReceiptWriteStatus.Accepted,
                Receipt = receipt
            };
        }

        var existing = await GetCoreAsync(receipt.Scope, receipt.SuspensionOperationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PostgreSQL receipt conflict did not return the accepted receipt.");
        return new WorkflowSuspensionReceiptWriteResult
        {
            Status = existing.Integrity == receipt.Integrity
                ? WorkflowSuspensionReceiptWriteStatus.Duplicate
                : WorkflowSuspensionReceiptWriteStatus.Conflict,
            Receipt = existing
        };
    }

    private async Task<WorkflowSuspensionReceipt?> GetCoreAsync(RuntimeTenantScope scope, string operationId, CancellationToken cancellationToken)
    {
        scope.EnsureValid();
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select receipt_json::text from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and operation_id=@operation;");
        PostgreSqlRuntimeStoreSupport.AddScope(command, scope);
        command.Parameters.AddWithValue("operation", operationId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return value is null
            ? null
            : PostgreSqlRuntimeStoreSupport.Deserialize(value, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowSuspensionReceipt);
    }

    private static void Validate(WorkflowSuspensionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        receipt.Scope.EnsureValid();
        receipt.WorkflowKey.EnsureValid();
        receipt.HumanTaskKey.EnsureValid();
        receipt.WorkflowPin.EnsureValid();
        receipt.HumanTaskPin.EnsureValid();
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt.SuspensionOperationId);
        if (receipt.WorkflowKey.TenantId != receipt.Scope.TenantId || receipt.HumanTaskKey.TenantId != receipt.Scope.TenantId)
            throw PostgreSqlRuntimeStoreSupport.Correlation("Suspension receipt keys must match the receipt tenant scope.");
    }
}
