using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Application.Approval;

public sealed class ErpSalesApprovalOrchestrator(
    IWorkflowEngine workflowEngine,
    IHumanTaskInstanceStore tasks,
    IRuntimeStateContractRegistry stateRegistry,
    IWorkflowAbortService abortService)
{
    public async Task<ErpApprovalWorkflowLease> StartAsync(
        SalesOrder order,
        CancellationToken cancellationToken = default)
    {
        var tenantId = order.TenantId == Guid.Empty ? null : order.TenantId.ToString();
        var workflow = await workflowEngine.ExecuteAsync(
            new WorkflowExecutionRequest
            {
                WorkflowId = ErpContractIds.SalesApprovalWorkflow,
                TenantId = tenantId,
                InputVariables = new Dictionary<string, RuntimeStateValue>
                {
                    ["salesOrderId"] = stateRegistry.Capture(order.Id)
                }
            },
            cancellationToken).ConfigureAwait(false);

        var pending = await tasks.GetPendingByWorkflowAsync(workflow.Key, cancellationToken).ConfigureAwait(false);
        if (workflow.Status != WorkflowInstanceStatus.Suspended
            || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId)
            || pending.Count != 1
            || !string.Equals(pending[0].Id, workflow.WaitingHumanTaskId, StringComparison.Ordinal)
            || workflow.WaitingHumanTaskKey is not { } humanTaskKey)
        {
            throw new InvalidOperationException(
                "Sales approval workflow must suspend with exactly one correlated pending HumanTask.");
        }

        return new ErpApprovalWorkflowLease(workflow.Key, humanTaskKey);
    }

    public Task AbortAsync(
        ErpApprovalWorkflowLease lease,
        string reason,
        CancellationToken cancellationToken = default)
        => abortService.AbortAsync(
            lease.WorkflowKey,
            lease.HumanTaskKey,
            reason,
            $"erp-sales-abort-{Guid.NewGuid():N}",
            cancellationToken);
}