using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Abstractions.Delivery;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryWorkflowAbortReceiptStore : IWorkflowAbortReceiptStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;

    public InMemoryWorkflowAbortReceiptStore(InMemoryRuntimeTransactionCoordinator coordinator)
        => _coordinator = coordinator;

    public Task<WorkflowAbortReceiptWriteResult> AddAsync(WorkflowAbortReceipt receipt, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ =>
        {
            using var guard = _coordinator.EnterStoreOperation();
            return ValueTask.FromResult(AddCore(receipt));
        }, cancellationToken).AsTask();

    public Task<WorkflowAbortReceipt?> GetAsync(RuntimeTenantScope scope, string abortOperationId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ =>
        {
            using var guard = _coordinator.EnterStoreOperation();
            if (!_coordinator.CurrentState.AbortReceipts.TryGetValue((scope, abortOperationId), out var receipt))
                return ValueTask.FromResult<WorkflowAbortReceipt?>(null);
            Validate(receipt);
            return ValueTask.FromResult<WorkflowAbortReceipt?>(receipt);
        }, cancellationToken).AsTask();

    private WorkflowAbortReceiptWriteResult AddCore(WorkflowAbortReceipt receipt)
    {
        Validate(receipt);
        var key = (receipt.Scope, receipt.AbortOperationId);
        if (_coordinator.CurrentState.AbortReceipts.TryGetValue(key, out var existing))
        {
            return new WorkflowAbortReceiptWriteResult
            {
                Status = existing.Integrity == receipt.Integrity
                    ? WorkflowAbortReceiptWriteStatus.Duplicate
                    : WorkflowAbortReceiptWriteStatus.Conflict,
                Receipt = existing
            };
        }

        _coordinator.CurrentState.AbortReceipts.Add(key, receipt);
        return new WorkflowAbortReceiptWriteResult
        {
            Status = WorkflowAbortReceiptWriteStatus.Accepted,
            Receipt = receipt
        };
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
        if (receipt.WorkflowKey.TenantId != receipt.Scope.TenantId
            || receipt.HumanTaskKey.TenantId != receipt.Scope.TenantId)
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Abort receipt keys must match the receipt tenant scope.");
        if (!WorkflowAbortReceiptCanonicalWriter.Matches(receipt))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "Abort receipt integrity does not match its immutable operation facts.");
    }
}
