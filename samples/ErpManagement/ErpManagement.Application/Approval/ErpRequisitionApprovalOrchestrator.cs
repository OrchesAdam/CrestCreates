using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;

namespace ErpManagement.Application.Approval;

/// <summary>Correlated workflow + HumanTask identities established for one requisition approval.</summary>
public sealed record ErpApprovalWorkflowLease(
    RuntimeInstanceKey WorkflowKey,
    RuntimeInstanceKey HumanTaskKey);

/// <summary>
/// Starts the formal single-step approval workflow for an over-threshold requisition and
/// asserts the suspend-with-exactly-one-pending-task invariant before the EF side records
/// the correlation. EF failure after this point is compensated through <see cref="AbortAsync"/>.
/// </summary>
public sealed class ErpRequisitionApprovalOrchestrator(
    IWorkflowEngine workflowEngine,
    IHumanTaskInstanceStore tasks,
    IRuntimeStateContractRegistry stateRegistry,
    IWorkflowAbortService abortService)
{
    public async Task<ErpApprovalWorkflowLease> StartAsync(
        PurchaseRequisition requisition,
        CancellationToken cancellationToken = default)
    {
        var tenantId = requisition.TenantId == Guid.Empty ? null : requisition.TenantId.ToString();
        var workflow = await workflowEngine.ExecuteAsync(
            new WorkflowExecutionRequest
            {
                WorkflowId = ErpContractIds.RequisitionApprovalWorkflow,
                TenantId = tenantId,
                InputVariables = new Dictionary<string, RuntimeStateValue>
                {
                    ["requisitionId"] = stateRegistry.Capture(requisition.Id)
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
                "Requisition approval workflow must suspend with exactly one correlated pending HumanTask.");
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
            $"erp-requisition-abort-{Guid.NewGuid():N}",
            cancellationToken);
}