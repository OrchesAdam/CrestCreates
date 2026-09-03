using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Abstractions.Delivery;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

/// <summary>
/// Uses the existing operation-receipt table with a reserved namespace. This
/// keeps abort evidence in the same provider transaction without changing the
/// frozen suspension-receipt schema.
/// </summary>
internal sealed class PostgreSqlWorkflowAbortReceiptStore : IWorkflowAbortReceiptStore
{
    private const string Prefix = "workflow-abort:";
    private readonly PostgreSqlRuntimePersistenceOptions _options;
    private readonly PostgreSqlRuntimeTransactionCoordinator _coordinator;
    private readonly string _table;

    public PostgreSqlWorkflowAbortReceiptStore(PostgreSqlRuntimePersistenceOptions options, PostgreSqlRuntimeTransactionCoordinator coordinator)
    {
        _options = options;
        _coordinator = coordinator;
        _table = PostgreSqlRuntimeStoreSupport.Table(options, "runtime_operation_receipts");
    }

    public Task<WorkflowAbortReceiptWriteResult> AddAsync(WorkflowAbortReceipt receipt, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowAbortReceiptWriteResult>(AddCoreAsync(receipt, ct)), cancellationToken).AsTask();

    public Task<WorkflowAbortReceipt?> GetAsync(RuntimeTenantScope scope, string abortOperationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(ct => new ValueTask<WorkflowAbortReceipt?>(GetCoreAsync(scope, abortOperationId, ct)), cancellationToken).AsTask();

    private async Task<WorkflowAbortReceiptWriteResult> AddCoreAsync(WorkflowAbortReceipt receipt, CancellationToken cancellationToken)
    {
        Validate(receipt);
        var operation = Prefix + receipt.AbortOperationId;
        var session = _coordinator.RequireSession();
        string? accepted;
        {
            using var commandLease = session.EnterCommand();
            await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
                $"insert into {_table} (tenant_scope_kind, tenant_id, operation_id, workflow_instance_id, human_task_instance_id, workflow_from_revision, workflow_to_revision, integrity_json, receipt_json) values (@scope, @tenant, @operation, @workflow, @task, @from, @to, @integrity, @receipt) on conflict (tenant_scope_kind, tenant_id, operation_id) do nothing returning receipt_json::text;");
            PostgreSqlRuntimeStoreSupport.AddScope(command, receipt.Scope);
            command.Parameters.AddWithValue("operation", operation);
            command.Parameters.AddWithValue("workflow", receipt.WorkflowKey.InstanceId);
            command.Parameters.AddWithValue("task", receipt.HumanTaskKey.InstanceId);
            command.Parameters.AddWithValue("from", receipt.WorkflowFromRevision);
            command.Parameters.AddWithValue("to", receipt.WorkflowToRevision);
            PostgreSqlRuntimeStoreSupport.AddJson(command, "integrity", PostgreSqlRuntimeStoreSupport.Serialize(receipt.Integrity, PostgreSqlRuntimeJsonSerializerContext.Default.CanonicalHash));
            PostgreSqlRuntimeStoreSupport.AddJson(command, "receipt", PostgreSqlRuntimeStoreSupport.Serialize(receipt, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowAbortReceipt));
            accepted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }

        if (accepted is not null)
            return new WorkflowAbortReceiptWriteResult { Status = WorkflowAbortReceiptWriteStatus.Accepted, Receipt = receipt };

        var existing = await GetCoreAsync(receipt.Scope, receipt.AbortOperationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PostgreSQL abort receipt conflict did not return the accepted receipt.");
        return new WorkflowAbortReceiptWriteResult
        {
            Status = existing.Integrity == receipt.Integrity ? WorkflowAbortReceiptWriteStatus.Duplicate : WorkflowAbortReceiptWriteStatus.Conflict,
            Receipt = existing
        };
    }

    private async Task<WorkflowAbortReceipt?> GetCoreAsync(RuntimeTenantScope scope, string abortOperationId, CancellationToken cancellationToken)
    {
        scope.EnsureValid();
        ArgumentException.ThrowIfNullOrWhiteSpace(abortOperationId);
        var session = _coordinator.RequireSession();
        using var commandLease = session.EnterCommand();
        await using var command = PostgreSqlRuntimeStoreSupport.CreateCommand(session, _options,
            $"select receipt_json::text from {_table} where tenant_scope_kind=@scope and tenant_id=@tenant and operation_id=@operation;");
        PostgreSqlRuntimeStoreSupport.AddScope(command, scope);
        command.Parameters.AddWithValue("operation", Prefix + abortOperationId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return value is null ? null : PostgreSqlRuntimeStoreSupport.Deserialize(value, PostgreSqlRuntimeJsonSerializerContext.Default.WorkflowAbortReceipt);
    }

    private static void Validate(WorkflowAbortReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        receipt.Scope.EnsureValid();
        receipt.WorkflowKey.EnsureValid();
        receipt.HumanTaskKey.EnsureValid();
        receipt.WorkflowPin.EnsureValid();
        receipt.HumanTaskPin.EnsureValid();
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt.AbortOperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt.Reason);
        if (receipt.WorkflowKey.TenantId != receipt.Scope.TenantId || receipt.HumanTaskKey.TenantId != receipt.Scope.TenantId)
            throw PostgreSqlRuntimeStoreSupport.Correlation("Abort receipt keys must match the receipt tenant scope.");
        if (!WorkflowAbortReceiptCanonicalWriter.Matches(receipt))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "Abort receipt integrity does not match its immutable operation facts.");
    }
}
