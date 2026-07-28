namespace CrestCreates.Sample.Procurement.Application;

public sealed record ProcurementApprovalWorkflowLease(
    string WorkflowInstanceId,
    string HumanTaskInstanceId);

public interface IProcurementApprovalOrchestrator
{
    Task<ProcurementApprovalWorkflowLease> StartAsync(
        Guid requestId,
        string tenantId,
        string requesterId,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        ProcurementApprovalWorkflowLease lease,
        CancellationToken cancellationToken = default);

    Task CompleteDecisionAsync(
        Guid requestId,
        string outcome,
        string comment,
        CancellationToken cancellationToken = default);
}
