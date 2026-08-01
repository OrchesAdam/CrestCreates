using CrestCreates.Runtime.Persistence.Abstractions.Keys;

namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowSuspensionReceiptStore
{
    Task<WorkflowSuspensionReceiptWriteResult> AddAsync(WorkflowSuspensionReceipt receipt, CancellationToken cancellationToken = default);
    Task<WorkflowSuspensionReceipt?> GetAsync(RuntimeTenantScope scope, string suspensionOperationId, CancellationToken cancellationToken = default);
}
