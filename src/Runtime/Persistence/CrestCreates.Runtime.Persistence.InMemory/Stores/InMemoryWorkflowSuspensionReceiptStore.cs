using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryWorkflowSuspensionReceiptStore : IWorkflowSuspensionReceiptStore
{
    private readonly Dictionary<(string? Tenant, string Operation), WorkflowSuspensionReceipt> _receipts = new();
    private readonly object _gate = new();
    public Task<WorkflowSuspensionReceiptWriteResult> AddAsync(WorkflowSuspensionReceipt receipt, CancellationToken cancellationToken = default)
    { lock (_gate) { var key = (receipt.Scope.TenantId, receipt.SuspensionOperationId); if (_receipts.TryGetValue(key, out var existing)) return Task.FromResult(new WorkflowSuspensionReceiptWriteResult { Status = existing.Integrity.Value == receipt.Integrity.Value ? WorkflowSuspensionReceiptWriteStatus.Duplicate : WorkflowSuspensionReceiptWriteStatus.Conflict, Receipt = existing }); _receipts[key] = receipt; return Task.FromResult(new WorkflowSuspensionReceiptWriteResult { Status = WorkflowSuspensionReceiptWriteStatus.Accepted, Receipt = receipt }); } }
    public Task<WorkflowSuspensionReceipt?> GetAsync(RuntimeTenantScope scope, string suspensionOperationId, CancellationToken cancellationToken = default)
    { lock (_gate) return Task.FromResult(_receipts.TryGetValue((scope.TenantId, suspensionOperationId), out var receipt) ? receipt : null); }
}
