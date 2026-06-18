namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowContinuationService
{
    Task ContinueAsync(WorkflowContinuationRequest request, CancellationToken ct = default);
}
