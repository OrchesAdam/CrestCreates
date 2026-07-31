using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryWorkflowSuspensionReceiptStore : IWorkflowSuspensionReceiptStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;

    public InMemoryWorkflowSuspensionReceiptStore(InMemoryRuntimeTransactionCoordinator coordinator)
        => _coordinator = coordinator;

    public Task<WorkflowSuspensionReceiptWriteResult> AddAsync(WorkflowSuspensionReceipt receipt, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(AddCore(receipt)); }, cancellationToken).AsTask();

    public Task<WorkflowSuspensionReceipt?> GetAsync(RuntimeTenantScope scope, string suspensionOperationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(_coordinator.CurrentState.Receipts.TryGetValue((scope, suspensionOperationId), out var receipt) ? receipt : null); }, cancellationToken).AsTask();

    private WorkflowSuspensionReceiptWriteResult AddCore(WorkflowSuspensionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        receipt.Scope.EnsureValid();
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt.SuspensionOperationId);

        var workflowKey = receipt.WorkflowKey;
        if (!_coordinator.CurrentState.Workflows.ContainsKey(workflowKey))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "Receipt references a non-existent Workflow instance.");

        var key = (receipt.Scope, receipt.SuspensionOperationId);
        if (_coordinator.CurrentState.Receipts.TryGetValue(key, out var existing))
        {
            return new WorkflowSuspensionReceiptWriteResult
            {
                Status = StructuredHashEquals(existing.Integrity, receipt.Integrity)
                    ? WorkflowSuspensionReceiptWriteStatus.Duplicate
                    : WorkflowSuspensionReceiptWriteStatus.Conflict,
                Receipt = existing
            };
        }

        _coordinator.CurrentState.Receipts.Add(key, receipt);
        return new WorkflowSuspensionReceiptWriteResult
        {
            Status = WorkflowSuspensionReceiptWriteStatus.Accepted,
            Receipt = receipt
        };
    }

    private static bool StructuredHashEquals(
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash left,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash right)
        => left == right;
}
